# -*- coding: utf-8 -*-

from flask import Flask, request, jsonify
import os
import requests
import spacy
import json
import threading
from dotenv import load_dotenv
from textblob import TextBlob
from transformers import pipeline
from collections import defaultdict
import re, random

# ======================================
# Bootstrap / Configuration
# ======================================

# Load environment variables from .env file
load_dotenv()

groq_api_key = os.getenv("GROQ_API_KEY")
max_tokens = int(os.getenv("MAX_TOKENS", 150))
base_temperature = float(os.getenv("TEMPERATURE", 0.7))
alien_profiles_path = os.getenv("ALIEN_PROFILES_PATH", "AlienPersonalities.json")
braxim_replies_path = os.getenv("BRAXIM_REPLIES_PATH", "BraximReplies.json")
state_path = os.getenv("STATE_PATH", "server_state.json")
save_every = int(os.getenv("SAVE_EVERY", "5")) # autosave every N chats

# ------------- GLOBAL STATE -------------
Q = defaultdict(lambda:defaultdict(float))                      # Q-table
alien_affect = defaultdict(lambda: {"joy": 0.0, "anger": 0.0})  # per-alien emotion state
closed_aliens = {}                                              # any "closed" flags which persist
STATE_LOCK = threading.Lock()

_update_count = 0
last_sa = defaultdict(lambda: None) # per-alien last (state, action)
last_dist = defaultdict(lambda: [0.2, 0.2, 0.2, 0.2, 0.2])

print(f"MAX TOKENS:", max_tokens)
print(f"TEMPERATURE:", base_temperature)
print(f"ALIEN PROFILES PATH:", alien_profiles_path)
print(f"BRAXIM REPLIES PATH:", braxim_replies_path)

# ======================================
# NLP Components
# ======================================

# Load SpaCy for NER
try:
    nlp_spacy = spacy.load("en_core_web_sm")
except Exception as e:
    print("Warning: Could not load 'en_core_web_sm'. Using blank English model. NER will be disabled", e)
    nlp_spacy = spacy.blank("en")

# Load Hugging Face sentiment/emotion pipeline
emotion_classifier = pipeline(
    "text-classification", 
    model="j-hartmann/emotion-english-distilroberta-base",
    top_k=None
)

# ======================================
# Reinforcement Learning Scafolding
# ======================================
INTENTS = [
    "build_rapport",    # warm, small talk, align values
    "seek_clarity",     # ask on brief clarifying question
    "apologize",        # de-escalate via apology/empathy
    "offer_trade",      # propose a concrete, low-stakes exchange
    "share_plan",       # outline a practical next step
    "close_treaty"      # attempt to conclude if mood is right
]

# Q[state][action] -> value
Q = defaultdict(lambda: {a: random.uniform(-0.05, 0.05) for a in INTENTS})
EPSILON = 0.15
ALPHA = 0.25

PAIR_RULE = {
    ("joy", "joy"):         +1.00,  # Ectasy
    ("joy", "sadness"):     +0.55,  # Melancholy
    ("joy", "disgust"):     +0.45,  # Intrigue
    ("joy", "fear"):        +0.85,  # Surprise
    ("joy", "anger"):       +0.30,  # Righteousness

    ("sadness", "sadness"): -0.80,  # Despair
    ("fear", "fear"):       -0.75,  # Terror
    ("disgust", "disgust"): -0.50,  # Prejudice
    ("anger", "anger"):     -1.00,  # Rage

    ("sadness", "fear"):    -0.55,  # Anxiety
    ("sadness", "disgust"): -0.55,  # Self-loating
    ("fear", "disgust"):    -0.55,  # Revulsion
    ("anger", "sadness"):   -0.90,  # Betrayal
    ("anger", "fear"):      -0.87,  # Hatred
    ("anger", "disgust"):   -0.60,  # Loathing
}

def bin_emotion(x, edges=(0.2, 0.5, 0.8)):
    # 0: low, 1:med, 2:high, 3:very-high
    if x < edges[0]: return 0
    if x < edges[1]: return 1
    if x < edges[2]: return 2
    return 3

def encode_state(raw_vec):
    # state from top-2 emotions + their bins
    # raw_vec is dict {label->score} normalized
    items = sorted(raw_vec.items(), key=lambda kv: kv[1], reverse=True)
    (e1, v1), (e2, v2) = items[0], items[1]
    return f"{e1}:{bin_emotion(v1)}|{e2}:{bin_emotion(v2)}"

def choose_intent(state):
    import random
    if random.random() < EPSILON:
        return random.choice(INTENTS)
    # greedy
    best = max(Q[state], key=lambda a: Q[state][a])
    return best

def _valence_arousal(dist):
    """
    Map the five emotions (joy, anger, sad, disgust, fear) 
    to continuous valence (pleasure) and arousal(activation).
    """
    joy = dist.get("joy", 0.0)
    disgust = dist.get("disgust", 0.0)
    anger = dist.get("anger", 0.0)
    fear = dist.get("fear", 0.0)
    sadness = dist.get("sadness", 0.0)

    # Valence: how pleasant/unpleasant the mixture is
    valence = (
        +1.00 * joy
        -0.85 * disgust
        -0.90 * anger
        -0.75 * fear
        -1.0 * sadness
    )

    # Arousal: anger/fear high, joy medium-high, disgust medium, sadness low
    arousal = (
        0.70 * joy
        +0.60 * disgust
        + 0.90 * anger
        + 0.80 * fear
        +0.20 * sadness
    )

    return max(-1.0, min(1.0, valence)), max(0.0, min(1.0, arousal))

def _pair_bonus(top1, v1, top2, v2):
    """
    Inside Out pair mapping (see disertation or GDD):
    - Joy+Joy (Ectasy). biggest positive
    - Joy+Sadness (Melancholy/Pity): positive
    - Joy+Disgust (Intrigue-ish redirection): positive
    - Joy+Anger (Righteousness): positive
    - Sadness+Sadness (Despair): negative
    - Sadness+Fear or Sadness+Disgust or Disgust+Fear (Anxiety/Revulsion-like): negative
    - Disgust+Disgust (Prejudice): lower negative
    - Fear+Fear (Terror): negative
    - Anger+Sadness (Betrayal): strong negative
    - Anger with anything except Joy: negative
    """
    pair = tuple(sorted([top1, top2])) # order-independent
    coef = PAIR_RULE.get(pair)
    if coef is None:
        #default: anger or disgust with anything else = mild negative
        coef = -0.35 if ("anger" in pair or "disgust" in pair) else 0.0
    return coef * 0.5 * (v1 + v2)

def compute_reward(dist):
    """
    dist: dict with keys 'disgust', 'fear', 'anger', 'sadness', 'joy' normalized to 1.0
    Returns a scalar reward in roughly [-2, +2]
    """

    # --------- Base continuous shaping ---------
    valence, arousal = _valence_arousal(dist)

    # Reward pleasentness; arousal only helps when valence >= 0 (energy in negative hurts)
    base = (1.20 * valence) + (0.40 * arousal * (1 if valence >= 0 else -0.3))

    # --------- Pair rule on top-2 emotions ---------
    items = sorted(dist.items(), key=lambda kv: kv[1], reverse=True)
    (e1, v1), (e2, v2) = items[0], items[1]
    pair_term = _pair_bonus(e1, v1, e2, v2)

    # --------- Extremes / terminals ---------
    joy = dist.get("joy", 0.0)
    anger = dist.get("anger", 0.0)
    sadness = dist.get("sadness", 0.0)

    if joy >= 0.85:     # Ectasy-ish
        base += +0.70
    if anger >= 0.85:   # Rage
        base += -1.10
    if sadness >= 0.80 and sadness == max(dist.values()):   # Despair
        base += -0.60
    
    reward = base + pair_term
    return float(max(-2.0, min(2.0, reward)))

def save_state():
    dir_ = os.path.dirname(state_path)
    if dir_:
        os.makedirs(dir_, exist_ok=True)

    # jsonify defaultdicts into plain dicts
    q_plain = {s: {a: float(v) for a, v in acts.items()} for s, acts in Q.items()}
    affect_plain = {k: {"joy": float(v.get("joy", 0.0)), "anger": float(v.get("anger", 0.0))}
                    for k, v in alien_affect.items()}
    last_plain = {k: [float(x) for x in v] for k, v in last_dist.items()}
    data = {
        "Q": q_plain,
        "alien_affect": affect_plain,
        "closed_aliens": closed_aliens,
        "last_dist": last_plain
    }

    with STATE_LOCK:
        with open(state_path, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2)

def load_state():
    """
    Load Q-table, affect, and closed_aliens from STATE_PATH
    If file is missing/empty/invalid, intialize with safe defaults
    """
    global Q, closed_aliens, last_dist, alien_affect

    dir_ = os.path.dirname(state_path)
    if dir_:
        os.makedirs(dir_, exist_ok=True)

    data = None
    try:
        with open(state_path, "r", encoding="utf-8") as f:
            raw = f.read().strip()
            data = json.loads(raw) if raw else None
    except Exception as e:
        print(f"[state] missing/invalid at {state_path}: {e}; starting fresh")
    
    if not data:
        data = {"Q": {}, "alien_affect": {}, "closed_aliens": {}, "last_dist": {}}
        with open(state_path, "w", encoding="utf-8") as f:
            json.dump(data, f)

    # Rebind Q with intent defaults
    Q = defaultdict(lambda: {a: 0.0 for a in INTENTS})
    for s, acts in data.get("Q", {}).items():
        Q[s] = {a: float(acts.get(a, 0.0)) for a in INTENTS}

    # running affect
    alien_affect.clear()
    for k, v in (data.get("alien_affect") or {}).items():
        alien_affect[k] = {
            "joy": float(v.get("joy", 0.0)), 
            "anger": float(v.get("anger", 0.0))
        }

    # closed flags
    closed_aliens = dict(data.get("closed_aliens") or {})

    # last donut sent to client
    last_dist.clear()
    for k, arr in (data.get("last_dist") or {}).items():
        arr = (arr + [0,0,0,0,0])[:5]
        last_dist[k] = [float(x) for x in arr]

# ======================================
# Alien Profiles
# ======================================
def load_alien_profiles(path: str):
    if not os.path.exists(path):
        print(f"AlienPersonalities.json not found at '{path}'. Creating a minimal default")
        default_profiles = [
            {
                "name": "Z1A-X0N",
                "personalityType": "Analyst-Diplomat",
                "description": "Crystalline collectivists who prize measured curiosity and harmony.",
                "culture": "Consensus-first councils; distrust unilateral human bravado.",
                "behaviorInstruction": "Calm, precise; prefers evidence and short declarative sentences.",
                "traits": {
                    "openness": 0.70,
                    "conscientiousness": 0.85,
                    "extraversion": 0.35,
                    "agreeableness": 0.70,
                    "neuroticism": 0.25
                },
                "joyThreshold": "0.82",
                "angerTolerance": "0.28"
            }
        ]
        with open(path, "w") as f:
            json.dump(default_profiles, f, indent=2)

    with open(path, "r") as f:
        arr = json.load(f)
        return {a["name"]: a for a in arr}

def load_braxim_replies(path):
    if not os.path.exists(path):
        return[]
    try:
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)

        # Normalize Braxim replies to a list of dictionaries
        out = []
        for item in data:
            intent = (item.get("intent") or "").strip()
            text = (item.get("text") or "").strip()
            kw = [k.lower() for k in (item.get("keywords") or [])]
            if text:
                out.append({ 
                    "intent": intent, 
                    "keywords": kw, 
                    "text": text
                })

        print(f"[Braxim] Loaded {len(out)} replies from {path}")
        return out

    except Exception as e:
        print(f"Error loading Braxim replies from {path}: {e}")
        return []

ALIENS = load_alien_profiles(alien_profiles_path)

# ======================================
# Braxim Static Replies Behaviour
# ======================================
BRAXIM_REPLIES = load_braxim_replies(braxim_replies_path)

_braxim_last_idx = None  # Last used Braxim reply index

_world_re = re.compile(r"[a-z']+")

def _tokens(st: str):
    return set(_world_re.findall((st or "").lower()))

def _guess_intent_from_text(st: str):
    t = (st or "").lower()
    if "?" in t or any(w in t for w in ["what","how","why","terms","clarify","explain","specify","conditions","details"]):
        return "seek_clarity"
    if any(w in t for w in ["sorry","apologize","regret","mistake","fault","apology","aton","forgive","contrition"]):
        return "apologize"
    if any(w in t for w in ["trade","offer","exchange","deal","swap","barter","price","sell","buy","bid","rates"]):
        return "offer_trade"
    if any(w in t for w in ["plan","proposal","ceasefire","schedule","timeline","withdraw","inspection","corridor","evacu","patrol","protocol","roadmap"]):
        return "share_plan"
    if any(w in t for w in ["sign","treaty","agreement","conclude","finalize","ratify","seal","accord","pact","signature"]):
        return "close_treaty"
    if any(w in t for w in ["peace","respect","cooperate","harmony","mutual","trust","culture","values","customs","esteem","friend"]):
        return "build_rapport"
    return None

def choose_braxim_reply(user_input: str, rl_intent: str, topk: int = 6):
    """ 
    Pick the best static line by (overlap + substring + RL bonus + heuristic guess).
    Then sample from the top-k best matches.
    """
    global _braxim_last_idx
    if not BRAXIM_REPLIES:
        return "We will reply formally once your terms are specific."

    U = _tokens(user_input)
    guess = _guess_intent_from_text(user_input)

    scored = []
    for i, item in enumerate(BRAXIM_REPLIES):
        kw = set(item["keywords"])

        #token overlap
        overlap = len(U & kw)

        # substring hits
        substr = sum(1 for k in kw if k in (user_input or "").lower())

        # bonuses
        rl_bonus = 0.4 if item["intent"] and item["intent"] == rl_intent else 0.0
        guess_bonus = 0.6 if guess and item["intent"] == guess else 0.0
        score = 1.0 * overlap + 0.3 * substr + rl_bonus + guess_bonus
        scored.append((score, i))

    scored.sort(key=lambda x: x[0], reverse=True)
    
    # Take top-k best matches
    top = [i for st, i in scored[:max(1, topk)] if st > 0]
    if not top:
        # still nothing matched; prefer guessed intent, then RL intent
        if guess:
            cand = [i for i, it in enumerate(BRAXIM_REPLIES) if it["intent"] == guess]
            if cand:
                top = cand
        if not top and rl_intent:
            cand = [i for i, it in enumerate(BRAXIM_REPLIES) if it["intent"] == rl_intent]
            if cand:
                top = cand

    if not top:
        # absolute fallback
        return "We will reply formally once your terms are specific."

    # avoid repeating the same exact line twice
    choices = [i for i in top if i != _braxim_last_idx] or [top[0]]
    idx = random.choice(choices)
    _braxim_last_idx = idx  # remember last used index
    return BRAXIM_REPLIES[idx]["text"]

# ======================================
# Penbol Social Behaviour
# ======================================
def penbol_social_cascade():
    """
    Recompute Penbol's mood from current network,
    even if we didn't talk with Penbol.
    """
    name = "PENBOL"
    profile = ALIENS.get(name)
    if not profile:
        return

    rel = profile.get("relations", {}) or {}
    SOCIAL_K = float(profile.get("socialK", 0.15))
    DECAY = 0.90  # decay factor for Penbol's mood

    aa = alien_affect[name] # running affect
    joy_gain = 0.0
    anger_gain = 0.0

    friend_score = 0.0
    for other, w in rel.items():
        # defaultdict gives 0.0 if missing
        other_affect = alien_affect[(other or "").upper()]
        other_joy = float(other_affect.get("joy", 0.0))
        other_anger = float(other_affect.get("anger", 0.0))
        friend_score += float(w) * (other_joy - other_anger)

    # Clamp and convert to gains
    friend_score = max(-1.0, min(1.0, friend_score))  # clamp to [-1, 1]
    if friend_score > 0:
        joy_gain = friend_score * SOCIAL_K
    elif friend_score < 0:
        anger_gain = (-friend_score) * SOCIAL_K

    # Apply with decay
    aa["joy"] = min(1.0, max(0.0, aa["joy"] * DECAY + joy_gain))
    aa["anger"] = min(1.0, max(0.0, aa["anger"] * DECAY + anger_gain))

    print(f"[Penbol cascade] friend_score={friend_score:.3f} joy->{aa['joy']:.3f} anger->{aa['anger']:.3f}")

# ======================================
# Helper methods
# ======================================
def trait_mix(traits):
    """

    Your mixed axes + conscientiousness retained:
        - EO: mix of Extraversion & Openness (curiosity/ assertiveness)
        - NA: mix of Neuroticism & Agreeableness (reactivity/defensiveness)
        - C: Conscientiousness (order/stability)
    """

    eo = 0.5 * (float(traits.get("extraversion", 0.5)) + float(traits.get("openness", 0.5)))
    na = 0.5 * (float(traits.get("neuroticism", 0.5)) + (1.0 - float(traits.get("agreeableness", 0.5))))
    c = float(traits.get("conscientiousness", 0.5))
    return eo, na, c

def style_hints_from_traits(traits):
    eo, na, c = trait_mix(traits)
    hints = []

    if eo > 0.6:
        hints.append("show curiosity; ask one short clarifying question")

    if na > 0.5:
        hints.append("use cautious, hedged language; stress collective safety")

    if float(traits.get("agreeableness", 0.5)) > 0.6:
        hints.append("be warm and cooperative")

    if c > 0.7:
        hints.append("propose a concrete next step or condition")

    return hints, eo, na, c

def redact_sensitive(text):
    """
    Replace personal entities in user input with fixed role-play values.
    - PERSON -> 'John'
    - GPE / LOC -> 'Missouri, US'
    - ORG -> 'Weyland-Yutani Corp'
    """
    RP_NAME = "John"
    RP_HOME = "Missouri, US"
    RP_COMPANY = "Weyland-Yutani Corp"

    if "ner" not in nlp_spacy.pipe_names:
        # Lightweight regex fallback for common self-diclosures
        st = text

        # Name patterns
        st = re.sub(r"\b(my\s+name\s+is|call\s+me)\s+[A-Za-z][\w'\-]+(?:\s+[A-Za-z][\w'\-]+){0,2}",
                   r"\1 " + RP_NAME, st, flags=re.I)
        st = re.sub(r"\b(i\s*am|i['’]m)\s+[A-Za-z][\w'\-]+",
                   r"\1 " + RP_NAME, st, flags=re.I)

        # Location patterns
        st = re.sub(r"\b(i\s*(?:am|['’]m)\s*from)\s+[^.,;!?]+",
                    r"\1 " + RP_HOME, st, flags=re.I)
        st = re.sub(r"\b(i\s*(?:live|am|['’]m)\s+in|i\s+was\s+born\s+in|born\s+in)\s+[^.,;!?]+",
                   r"\1 " + RP_HOME, st, flags=re.I)

        # Organization patterns
        st = re.sub(r"\b(i\s*(?:work|serve|am\s+employed)\s+(?:at|for))\s+[^.,;!?]+",
                    r"\1 " + RP_COMPANY, st, flags=re.I)
        st = re.sub(r"\b(my\s+(?:employer|company|organization)\s+is)\s+[^.,;!?]+",
                    r"\1 " + RP_COMPANY, st, flags=re.I)

        return st, []

    doc = nlp_spacy(text)
    named = [{"text": ent.text, "label": ent.label_} for ent in doc.ents]

    # Replace entities inline (PERSON, GPE, ORG, LOC)
    st = text
    for ent in sorted(doc.ents, key=lambda e: e.start_char, reverse=True):
        if ent.label_ in("PERSON",):
            st = st[:ent.start_char] + RP_NAME + st[ent.end_char:]
        elif ent.label_ in ("GPE", "LOC"):
            st = st[:ent.start_char] + RP_HOME + st[ent.end_char:]
        elif ent.label_ == "ORG":
            st = st[:ent.start_char] + RP_COMPANY + st[ent.end_char:]

    # Also handle "my name is .. / I live in .." lexical pattern
    st = re.sub(r"\b(my\s+name\s+is|call\s+me)\s+[A-Za-z][\w'\-]+(?:\s+[A-Za-z][\w'\-]+){0,2}",
               r"\1 " + RP_NAME, st, flags=re.I)
    st = re.sub(r"\b(i\s*am|i['’]m)\s+[A-Za-z][\w'\-]+",
               r"\1 " + RP_NAME, st, flags=re.I)
    st = re.sub(r"\b(i\s*(?:am|['’]m)\s*from)\s+[^.,;!?]+",
               r"\1 " + RP_HOME, st, flags=re.I)
    st = re.sub(r"\b(i\s*(?:live|am|['’]m)\s+in|i\s+was\s+born\s+in|born\s+in)\s+[^.,;!?]+",
               r"\1 " + RP_HOME, st, flags=re.I)
    st = re.sub(r"\b(i\s*(?:work|serve|am\s+employed)\s+(?:at|for))\s+[^.,;!?]+",
                r"\1 " + RP_COMPANY, st, flags=re.I)
    st = re.sub(r"\b(my\s+(?:employer|company|organization)\s+is)\s+[^.,;!?]+",
                r"\1 " + RP_COMPANY, st, flags=re.I)
    
    return st, named

def top_emotion_classifier(raw_result):
    """

    Handles HF pipeline outputs: either a list of dicts, or list-of-list form.
    Returns (label, score) in lowercase label.
    """
    if isinstance(raw_result, list) and raw_result and isinstance(raw_result[0], list):
        item = raw_result[0][0]  # Get the first result
        return item['label'].lower(), float(item["score"])
    if isinstance(raw_result, list) and raw_result:
        item = raw_result[0]
        return item['label'].lower(), float(item["score"])
    if isinstance(raw_result, dict):
        return raw_result['label'].lower(), float(raw_result["score"])
    return "unknown", 0.0

def behavior_from_emotion(top_emotion, score):
    if top_emotion == "joy" and score > 0.7:
        return "Answer with joy and agreement, sounding cheerful and optimistic."
    elif top_emotion == "sadness":
        return "Respond with empathy and gentle reassurance."
    elif top_emotion == "anger":
        return "Sound cautious, defensive, and wary."
    elif top_emotion == "fear":
        return "Express concern and emphasize caution and distrust of humans."
    elif top_emotion == "disgust":
        return "Sound polite, brief, detached; show discomfort and redirect"
    else:
        return "Maintain a calm, balanced diplomatic tone."

def adjusted_temperature(base_temp, conscientiousness):
    # Higher conscientiousness -> more stable tone -> reduce randomness
    return max(0.2, base_temp - 0.3 * (conscientiousness - 0.5))

# ======================================
# Flask App
# ======================================
app = Flask(__name__)
load_state()

@app.route('/chat', methods=['POST'])
def chat():
    data = request.get_json() or {}
    user_input = (data.get("message") or "").strip()
    alien_name = (data.get("alienName") or "ZAXIN").strip().upper()

    if not user_input:
        return jsonify({"error": "Empty message"}), 400

    profile = ALIENS.get(alien_name)
    if profile is None:
        return jsonify({"error": f"Alien profile '{alien_name}' not found"}), 400

    # If talks already concluded for this alien, refuse to continue
    if alien_name in closed_aliens:
        outcome = closed_aliens[alien_name]
        joy_threshold = float(profile.get("joyThreshold", 0.9))
        anger_tolerance = float(profile.get("angerTolerance", 0.3))
        return jsonify({
            "reply": "The council considers this matter settled.",
            "analysis": {
                "entities": [],
                "emotion": "neutral",
                "emotionScore": 0.0,
                "distributionJson": json.dumps({"keys": ["disgust", "fear", "anger", "sadness", "joy"], "values": [0,0,0,0,0]}),
                "polarity": 0.0,
                "subjectivity": 0.0
            },
            "alienProfile": {
                "name": profile["name"],
                "joyThreshold": joy_threshold,
                "angerTolerance": anger_tolerance,
            },
            "state": {
                "joy": alien_affect[alien_name]["joy"],
                "anger": alien_affect[alien_name]["anger"],
            },
            "styleHints": [],
            "temperatureUsed": base_temperature,
            "negotiationSuccess": outcome["success"],
            "negotiationFailure": outcome["failure"],
            "rl": None
    })

    # --- Redaction & NER ---
    redacted_input, named_entities = redact_sensitive(user_input)

    # --- Sentiment Analysis ---
    tb = TextBlob(user_input)
    polarity = float(tb.sentiment.polarity)
    subjectivity = float(tb.sentiment.subjectivity)

    # --- Emotion Analysis ---
    emotion_results = emotion_classifier(user_input)
    print("Emotion results raw output:", emotion_results)
    top_emotion, emotion_score = top_emotion_classifier(emotion_results)

    print(f"Redacted input: {redacted_input}")
    print(f"Entities: {named_entities}")
    print(f"Polarity: {polarity}")
    print(f"Emotion: {top_emotion} ({emotion_score:.3f})")

    # Build ordered canonical distribution
    canon_order = ["disgust", "fear", "anger", "sadness", "joy"]

    arr = []
    if isinstance(emotion_results, list):
        if emotion_results and isinstance(emotion_results[0], list):
            arr = emotion_results[0]
        else:
            arr = emotion_results
    elif isinstance(emotion_results, dict):
        arr = [emotion_results]

    raw = {d['label'].lower(): float(d['score']) for d in arr}
    vals = [raw.get(k, 0.0) for k in canon_order]
    s = sum(vals) or 1.0
    vals = [v / s for v in vals]

    # ======================================
    # RL State/Action Update and Round-Trip
    # ======================================
    dist_map = dict(zip(canon_order, vals))

    # credit the previous (state, action) with the current reward (delayed credit)
    prev = last_sa.get(alien_name)
    if prev:
        prev_state, prev_action = prev
        r = compute_reward(dist_map)
        Q_prev = Q[prev_state][prev_action]
        Q[prev_state][prev_action] = Q_prev + ALPHA * (r - Q_prev)

    rl_state = encode_state(dist_map)
    rl_action = choose_intent(rl_state)

    # Braxim uses static replies, while Zaxim/Penbol use LLM
    is_braxim = alien_name.upper() == "BRAXIM"

    distribution_json = json.dumps({
        "keys": canon_order,
        "values": vals
    })

    # --- Persona & style ---
    style_hints, eo, na, c = style_hints_from_traits(profile.get("traits", {}))
    behavior_instruction = behavior_from_emotion(top_emotion, emotion_score)
    temp = adjusted_temperature(base_temperature, c)

    if not is_braxim:
        # --- System prompt ---
        social_line = ""
        if alien_name.upper() == "PENBOL":
            rel = profile.get("relations", {}) or {}
            likes = [k for k,v in rel.items() if v > 0]
            dislikes = [k for k, v in rel.items() if v < 0]
            if likes or dislikes:
                social_line = ("social context: you currently like " + ", ".join(likes) if likes else "") + \
                    ((" and dislike " if likes and dislikes else "dislike ") + ", ".join(dislikes) if dislikes else "") + \
                    ". Let this subtly color your tone."
        system_prompt = f"""
        You are {profile['name']}, the alien leader.
        Persona: {profile.get('personalityType','')}.
        Description: {profile.get('description', '')}.
        Culture: {profile.get('culture', '')}.
        Stay in-character. {profile.get('behaviorInstruction', '')}
        Style hints: {", ".join(style_hints)}.
        {social_line}

        Player emotion: {top_emotion} ({emotion_score:.2f}); sentiment polarity: {polarity:.2f}, subjectivity: {subjectivity:.2f}.
        Player said: {redacted_input}.
        {behavior_instruction}
        Current diplomatic intent: {rl_action}.
        Keep your answer concise and very briefly. Use up to {max_tokens} tokens. 
        Always end your response with a complete sentence.
        """.strip()

        # ---- Conversation history from client ----
        raw_hist = data.get("history", []) or []

        # Santize and clip (defensive)
        hist = []
        if isinstance(raw_hist, (list, tuple)):
            for t in list(raw_hist)[-16:]:
                role = (t.get("role") or "").strip()
                content = (t.get("content") or "").strip()
                if not content:
                    continue
                if role not in ("user", "assistant"): # Keep history simple; drop extra roles
                    continue
                hist.append({"role": role, "content": content})

        messages = [{"role": "system", "content": system_prompt}]
        messages.extend(hist)
        messages.append({"role": "user", "content": redacted_input})
    
        print(f"==== System prompt ====")
        print(system_prompt)
        print(f"=====================")


        # --- Call Groq API ---
        headers = {
            "Authorization": f"Bearer {groq_api_key}",
            "Content-Type": "application/json"
        }

        payload = {
            "model": "llama3-8b-8192",
            "messages": messages,
            "temperature": temp,
            "max_tokens": max_tokens
        }

        response = None
        try:
            response = requests.post(
                "https://api.groq.com/openai/v1/chat/completions",
                headers=headers,
                json=payload,
                timeout=30  # Set a timeout for the request
            )
            response.raise_for_status()
            reply = response.json()["choices"][0]["message"]["content"]

        
        except requests.exceptions.HTTPError as err:
            # Response may be None if post() itself failed
            body = response.text if response is not None else ""
            print("Groq request failed:", err, body)
            return jsonify({"error": "Failed to contact Groq API"}), 500
        except Exception as e:
            print("Groq request error:", e)
            return jsonify({"error": "Groq request error"}), 500

    else:
        reply = choose_braxim_reply(user_input, rl_action)

    # ======================================
    # AFFECT DYNAMICS (per alien)
    # ======================================

    # Personality-biased, but modest, steps
    JOY_STEP = 0.25
    ANGER_STEP = 0.25
    DECAY = 0.90

    traits = profile.get("traits", {})
    ag = float(traits.get("agreeableness", 0.5))

    # Personality-biased gains
    anger_gain = 0.0
    if top_emotion == "anger":
        anger_gain = emotion_score * ANGER_STEP * (0.8 + 0.6 * na - 0.4 * ag)

    joy_gain = 0.0
    if top_emotion == "joy":
        joy_gain = emotion_score * JOY_STEP * (0.8 + 0.4 * ag - 0.2 * na)

    # Damp tiny inputs (e.g., "hi") so they don't swing mood
    if len(user_input.split()) <= 2:
        anger_gain *= 0.25
        joy_gain *= 0.25

    # Social influence (Penbol only): friends' moods sway Penbol
    if alien_name.upper() == "PENBOL":
        rel = profile.get("relations", {}) or {}

        # How much Penbol cares about friends' moods)
        SOCIAL_K = float(profile.get("socialK", 0.15))

        # friend_score > 0 if liked aliens are joyful (or disliked rivals are misserable)
        friend_score = 0.0
        for other_name, w in rel.items():
            other_key = (other_name or "").upper()

            #defaultdict gives 0.0 if missing
            other_affect = alien_affect[other_key]

            other_joy = float(other_affect.get("joy", 0.0))
            other_anger = float(other_affect.get("anger", 0.0))
            friend_score += float(w) * (other_joy - other_anger)

        friend_score = max(-1.0, min(1.0, friend_score))  # clamp to [-1, 1]
        # Apply social influence
        if friend_score > 0:
            joy_gain += friend_score * SOCIAL_K
        elif friend_score < 0:
            anger_gain += (-friend_score) * SOCIAL_K


    # Passive decay towards 0 (feelings fade)
    alien_affect[alien_name]["anger"] = min(1.0, max(0.0, alien_affect[alien_name]["anger"] * DECAY + anger_gain))
    alien_affect[alien_name]["joy"] = min(1.0, max(0.0, alien_affect[alien_name]["joy"] * DECAY + joy_gain))

    # Gates
    # Track momentum
    aa = alien_affect[alien_name]
    aa.setdefault("turns", 0)
    aa.setdefault("joy_streak", 0)
    aa.setdefault("anger_streak", 0)
    aa["turns"] += 1
    aa["joy_streak"] = aa["joy_streak"] + 1 if top_emotion == "joy" and emotion_score >= 0.6 else 0
    aa["anger_streak"] = aa["anger_streak"] + 1 if top_emotion == "anger" and emotion_score >= 0.6 else 0

    min_turns = int(profile.get("minTurnsToConclude", 3))
    joy_threshold = float(profile.get("joyThreshold", 0.9))
    anger_tolerance = float(profile.get("angerTolerance", 0.3))

    success = (aa["joy"] >= joy_threshold and aa["joy_streak"] >= 2 and aa["turns"] >= min_turns)
    failure = (aa["anger"] >= anger_tolerance and aa["anger_streak"] >= 2 and aa["turns"] >= min_turns)

    if success and alien_name not in closed_aliens:
        closed_aliens[alien_name] = {
            "success": True,
            "failure": False,
            "message": "Diplomatic solution reached. Talks Concluded."
        }
    elif failure and alien_name not in closed_aliens:
        closed_aliens[alien_name] = {
            "success": False,
            "failure": True,
            "message": "Negotiation failed. The alien refuses to continue."
        }

    # keep Penbol socially up-to-date when we talk to other aliens
    if alien_name.upper() != "PENBOL":
        penbol_social_cascade()

    # Log current state
    print(f"[{alien_name}] joy={alien_affect[alien_name]['joy']:.3f} / thr={joy_threshold} | "
          f"anger={alien_affect[alien_name]['anger']:.3f} / tol={anger_tolerance} | "
          f"success={success} failure={failure}")

    rl_reward = compute_reward(dist_map)
    Q_prev = Q[rl_state][rl_action]
    Q[rl_state][rl_action] = Q_prev + ALPHA * (rl_reward - Q_prev)

    # Build the distribution to send back to Unity (with Penbol social overlay)
    display_vals = list(vals)  # copy to avoid mutation
    joy_i = canon_order.index("joy")
    anger_i = canon_order.index("anger")

    if alien_name.upper() == "PENBOL":
        aa = alien_affect[alien_name] # Penbol's running mood after social influence/math above
        overlay_strength = float(profile.get("socialOverlay", 0.5))

        # Lift joy/anger visually to reflect current mood (gentle, not overriding)
        display_vals[joy_i] = max(display_vals[joy_i], overlay_strength * float(aa.get("joy", 0.0)))
        display_vals[anger_i] = max(display_vals[anger_i], overlay_strength * float(aa.get("anger", 0.0)))

        # Renormalize to sum to 1
        s = sum(display_vals) or 1.0
        display_vals = [v / s for v in display_vals]
    else:
        display_vals = list(vals)  # just copy the original values

    distribution_json = json.dumps({"keys": canon_order, "values": display_vals})
    
    # remember the exact donut sent to the client
    last_dist[alien_name] = list(display_vals)

    # remember (state, action) for next trun's delayed credit
    last_sa[alien_name] = (rl_state, rl_action)

    # Autosave occasionally
    global _update_count
    _update_count += 1
    if _update_count % save_every == 0:
        save_state()


    # ======================================
    # Response
    # ======================================
    return jsonify({
        "reply": reply,
        "analysis": {
            "entities": named_entities,
            "emotion": top_emotion,
            "emotionScore": emotion_score,
            "distributionJson": distribution_json,
            "polarity": polarity,
            "subjectivity": subjectivity,
        },
        "alienProfile": {
            "name": profile["name"],
            "personalityType": profile.get("personalityType", ""),
            "description": profile.get("description", ""),
            "culture": profile.get("culture", ""),
            "behaviorInstruction": profile.get("behaviorInstruction", ""),
            "joyThreshold": joy_threshold,
            "angerTolerance": anger_tolerance,
        },
        "state": {
            "joy": alien_affect[alien_name]["joy"],
            "anger": alien_affect[alien_name]["anger"],
        },
        "styleHints": style_hints,
        "temperatureUsed": temp,
        "negotiationSuccess": success,
        "negotiationFailure": failure,
        "rl": {
            "stateKey": rl_state,
            "intent": rl_action,
            "reward": rl_reward,
            "qForState": Q[rl_state]
        }
    })
    

# Endpoint to reset affect during testing
@app.route('/reset_affect', methods=['POST'])
def reset_affect():
    global alien_affect, last_dist, closed_aliens

    payload = request.get_json(silent=True) or {}
    name = (payload.get("alienName") or "").strip().upper()

    NEUTRAL_DIST = [0.2,0.2,0.2,0.2,0.2]
    NEUTRAL_AFFECT = {
        "disgust": 0.0, "fear": 0.0, "anger": 0.0, "sadness": 0.0, "joy": 0.0
    }

    with STATE_LOCK:
        if name:
            if name not in ALIENS:
                return jsonify({"ok": False, "error": f"Unknown alien '{name}'"}), 400
            #safest: drop running affect; neutralize donut; clear closed flag
            alien_affect.pop(name, None)
            closed_aliens.pop(name, None)
            last_dist[name] = NEUTRAL_DIST.copy()
            last_sa.pop(name, None)
        else:
            # fresh run for everyone
            alien_affect.clear()
            closed_aliens.clear()
            last_sa.clear()
            # seed neutral donut for all aliens so charts are correct before first chat
            for n in ALIENS.keys():
                last_dist[n.upper()] = NEUTRAL_DIST.copy()
        save_state()
    return jsonify({"ok": True})



@app.route('/alien_state', methods=['POST'])
def alien_state():
    data = request.get_json() or {}
    name = (data.get("alienName") or "").strip().upper()
    profile = ALIENS.get(name)
    if not profile:
        return jsonify({"error": f"Alien profile '{name}' not found"}), 400

    # Base display distribution (neutral prior)
    canon_order = ["disgust", "fear", "anger", "sadness", "joy"]

    # Overlay running mood
    joy_i = canon_order.index("joy")
    anger_i = canon_order.index("anger")
    fear_i = canon_order.index("fear")
    sad_i = canon_order.index("sadness")
    disgust_i = canon_order.index("disgust")

    # start from the last donut we sent for this alien
    display_vals = list(last_dist.get(name, [0.2,0.2,0.2,0.2,0.2]))

    # gently reflect current running affect (joy/anger) for all aliens
    aa = alien_affect[name]
    base_overlay = float(profile.get("baseOverlay", 0.35)) # tweakable per-alien, default gentle
    display_vals[joy_i] = max(display_vals[joy_i], base_overlay * float(aa.get("joy", 0.0)))
    display_vals[anger_i] = max(display_vals[anger_i], base_overlay * float(aa.get("anger", 0.0)))

    # Penbol: add social visualization using relations
    if name == "PENBOL":
        overlay = float(profile.get("socialOverlay", 0.7))

        # recompute friend score love for display
        rel = profile.get("relations", {}) or {}
        friend_score = 0.0
        for other, w in rel.items():
            other_feel = alien_affect[(other or "").upper()]
            friend_score += float(w) * (float(other_feel.get("joy", 0.0)) - float(other_feel.get("anger", 0.0)))
        friend_score = max(-1.0, min(1.0, friend_score))

        # Positive social -> boost joy visually; Negative -> redistribute into negatives + reduce joy
        if friend_score > 0.0:
            pos = overlay * friend_score
            display_vals[joy_i] += pos
            # slightly de-emphasis of anger
            display_vals[anger_i] *= (1.0 - 0.25 * pos)
        elif friend_score < 0.0:
            neg = overlay * (-friend_score)
            # pull down joy a bit
            display_vals[joy_i] *= (1.0 - 0.40 * neg)
            # push into negative emotions (anger > disgust > fear); sadness small
            display_vals[anger_i] += 0.50 * neg 
            display_vals[disgust_i] += 0.30 * neg
            display_vals[fear_i] += 0.20 * neg
            display_vals[sad_i] += 0.10 * neg

    # Normalize
    s = sum(display_vals) or 1.0
    display_vals = [v / s for v in display_vals]

    return jsonify({
        "alien": name,
        "state": {"joy": aa.get("joy", 0.0), "anger": aa.get("anger", 0.0)},
        "distributionJson": json.dumps({
            "keys": canon_order,
            "values": display_vals
        }),
        "joyThreshold": float(profile.get("joyThreshold", 0.9)),
        "angerTolerance": float(profile.get("angerTolerance", 0.3)),
    })

@app.route('/health', methods=['GET'])
def health(): 
    return jsonify({"ok": True}), 200

@app.route('/save', methods=['POST'])
def save_now():
    save_state();
    return jsonify({"ok": True})

if __name__ == '__main__':
    app.run(port=5000)