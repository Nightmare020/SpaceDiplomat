# -*- coding: utf-8 -*-

from flask import Flask, request, jsonify
from langdetect import detect, DetectorFactory
import os
import requests
import spacy
import json
from dotenv import load_dotenv
from textblob import TextBlob
from transformers import pipeline
from collections import defaultdict
from datetime import datetime
import threading, pathlib
import re, random

# ======================================
# Bootstrap / Configuration
# ======================================
DetectorFactory.seed = 0  # stable detections
# Load environment variables from .env file
load_dotenv()

groq_api_key = os.getenv("GROQ_API_KEY")
groq_model_id = os.getenv("GROQ_MODEL_ID", "llama-3.1-8b-instant")
max_tokens = int(os.getenv("MAX_TOKENS", 150))
base_temperature = float(os.getenv("TEMPERATURE", 0.7))
alien_profiles_path = os.getenv("ALIEN_PROFILES_PATH", "AlienPersonalities.json")
braxim_replies_path = os.getenv("BRAXIM_REPLIES_PATH", "BraximReplies.json")

print(f"MAX TOKENS:", max_tokens)
print(f"TEMPERATURE:", base_temperature)
print(f"ALIEN PROFILES PATH:", alien_profiles_path)
print(f"BRAXIM REPLIES PATH:", braxim_replies_path)

last_display_dist = defaultdict(lambda: {"keys": ["disgust", "fear", "anger", "sadness", "joy"], 
                                         "values":[0.2,0.2,0.2,0.2,0.2]})

# ======================================
# RL Persistence
# ======================================
DATA_DIR = pathlib.Path(os.getenv("DATA_DIR", "/opt/space-diplomat/app/data")).resolve()
LOG_DIR = DATA_DIR / "logs"
Q_PATH = DATA_DIR / "q_table.json"
_io_lock = threading.Lock()

DATA_DIR.mkdir(parents=True, exist_ok=True)
LOG_DIR.mkdir(parents=True, exist_ok=True)

def load_q_table():
    global Q
    if Q_PATH.exists():
        try:
            with Q_PATH.open("r", encoding="utf-8") as f:
                table = json.load(f)

            # restore defaults for missing actions
            for s, acts in table.items():
                for a in INTENTS:
                    acts.setdefault(a, 0.0)
            Q.clear()
            for s, acts in table.items():
                Q[s] = acts

            print(f"[RL] Loaded Q-table with {len(Q)} states from {Q_PATH}")
        except Exception as e:
            print("[RL] Could not load Q-table:", e)

def save_q_table():
    # write automatically
    tmp = Q_PATH.with_suffix(".tmp")
    with _io_lock:
        with tmp.open("w", encoding="utf-8") as f:
            json.dump(Q, f, ensure_ascii=False, indent=2)
        tmp.replace(Q_PATH)

def append_rl_log(event: dict):
    ts = datetime.utcnow().strftime("%Y%m%d")
    path = LOG_DIR / f"rl_interactions_{ts}.jsonl"
    with _io_lock:
        with path.open("a", encoding="utf-8") as f:
            f.write(json.dumps(event, ensure_ascii=False) + "\n")

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
load_q_table()
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

# Running affect per alien
alien_affect = defaultdict(lambda: {"joy": 0.0, "anger": 0.0})

# Aliens that are closed (talks concluded)
closed_aliens = {}

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

@app.route('/chat', methods=['POST'])
def chat():
    data = request.get_json() or {}
    user_input = (data.get("message") or "").strip()
    alien_name = data.get("alienName", "Z1A-X0N")
    session_id = (data.get("sessionId") or "").strip()    # from client

    # snapshot affect before this turn
    alien_before = dict(alien_affect[alien_name])         # joy/anger for target alien
    penbol_before = dict(alien_affect["PENBOL"])          # joy/anger for Penbol (social mood)

    if not user_input:
        return jsonify({"error": "Empty message"}), 400

    try:
        lang_code = detect(user_input)
    except Exception:
        lang_code = "en"

    lang_hint = "Always respond in the user's language; for this turn, reply in {}.".format(lang_code)

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
    raw = {d['label'].lower(): float(d['score']) for d in emotion_results[0]}
    vals = [raw.get(k, 0.0) for k in canon_order]
    s = sum(vals) or 1.0
    vals = [v / s for v in vals]

    # ======================================
    # RL State/Action Update and Round-Trip
    # ======================================
    dist_map = dict(zip(canon_order, vals))
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
        {lang_hint}
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
            "model": groq_model_id,
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
            status = err.response.status_code if err.response is not None else None
            detail = ""
            try:
                detail = err.response.text[:400] if err.response is not None else ""
            except Exception:
                pass
            app.logger.error("Groq HTTP %s: %s", status, detail)
            return jsonify({"error":"groq_http_error","status":status,"detail":detail}), 502
        
        except Exception as e:
            app.logger.exception("Groq request error")
            return jsonify({"error":"groq_request_error","detail":str(e)}), 502

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
    if top_emotion in ("anger", "sadness", "fear", "disgust"):
        # treat any non-joy as negative for Penbol rules
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
        SOCIAL_K = float(profile.get("socialK", 0.35))

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

    # Snapshot affect after this turn
    alien_after = dict(alien_affect[alien_name])
    penbol_after = dict(alien_affect["PENBOL"])

    try:
        append_rl_log({
            "ts": datetime.utcnow().isoformat(timespec="seconds")+"Z",
            "sessionId": session_id,
            "alien": alien_name,
            "userInputRedacted": redacted_input,       # keep PII out
            "reply" : reply,
            "topEmotion": top_emotion,
            "emotionScore": emotion_score,
            "dist": dict(zip(canon_order, vals)),
            "polarity": polarity,
            "subjectivity": subjectivity,
            "rl": {
                "state": rl_state,
                "action": rl_action,
                "reward": rl_reward,
                "qBefore": Q_prev,
                "qAfter": Q[rl_state][rl_action]
            },
            "affect": {
                "alienBefore": alien_before,
                "alien_after": alien_after,
                "penbolBefore": penbol_before,
                "penbolAfter": penbol_after
            },
            "success": success,
            "failure": failure
        })
    except Exception as e:
        app.logger.exception("RL logging failed: %s", e)

    try:
        save_q_table()
    except Exception as e:
        app.logger.exception("Q-table save failed: %s", e)

    # Build the distribution to send back to Unity (with Penbol social overlay)
    display_vals = list(vals)  # copy to avoid mutation
    joy_i = canon_order.index("joy")
    anger_i = canon_order.index("anger")
    fear_i = canon_order.index("fear")
    sad_i = canon_order.index("sadness")
    disgust_i = canon_order.index("disgust")

    if alien_name.upper() == "PENBOL":
        aa = alien_affect[alien_name] # Penbol's running mood after social influence/math above
        overlay_strength = float(profile.get("socialOverlay", 0.5))

        # Lift joy/anger visually to reflect current mood (gentle, not overriding)
        display_vals[joy_i] += overlay_strength * float(aa.get("joy", 0.0))
        display_vals[anger_i] += overlay_strength * float(aa.get("anger", 0.0))

        # Renormalize to sum to 1
        s = sum(display_vals) or 1.0
        display_vals = [v / s for v in display_vals]
    else:
        display_vals = list(vals)  # just copy the original values

    distribution_json = json.dumps({"keys": canon_order, "values": display_vals})
   
    last_display_dist[alien_name.upper()] = {"keys": canon_order, "values": display_vals}


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
    data = request.get_json() or {}
    name = (data.get("alienName") or "").strip().upper()

    # helper: neutral 5-way split
    def _neutral():
        return {"keys": ["disgust", "fear", "anger", "sadness", "joy"], 
                "values": [0.2,0.2,0.2,0.2,0.2]}

    if name:
        alien_affect.pop(name, None)
        closed_aliens.pop(name, None)
        last_display_dist[name] = _neutral()
        return jsonify({"ok": True, "reset": name})
    else:
        # Reset all aliens
        alien_affect.clear()
        closed_aliens.clear()
        # reset the cached donut baseline for everyone
        for k in list(last_display_dist.keys()):
            last_display_dist[k] = _neutral()
        return jsonify({"ok": True, "reset": "ALL"})
    return jsonify({"ok": False, "error": "Unknown alien"}), 400


@app.route('/alien_state', methods=['POST'])
def alien_state():
    try:
        data = request.get_json() or {}
        name = (data.get("alienName") or "").strip().upper()
        profile = ALIENS.get(name)
        if not profile:
            return jsonify({"error": f"Alien profile '{name}' not found"}), 400

        # keep Penbol's running mood up-to-date
        if name == "PENBOL":
            penbol_social_cascade() # keep Penbol's social mood fresh

        # Base display distribution (neutral prior)
        canon_order = ["disgust", "fear", "anger", "sadness", "joy"]

        # use last display_display_dist if we have it; otherwise neutral
        base = last_display_dist.get(
            name,
            {"keys": canon_order, "values": [0.2, 0.2, 0.2, 0.2, 0.2]}
        )
        display_vals = list(base["values"])

        # Add Penbol's social overlay (additive), the renormalize
        aa = alien_affect[name]
        if name == "PENBOL":
            overlay = float(profile.get("socialOverlay", 0.5))
            joy_i = canon_order.index("joy")
            anger_i = canon_order.index("anger")
            display_vals[joy_i] += overlay * float(aa.get("joy", 0.0))
            display_vals[anger_i] += overlay * float(aa.get("anger", 0.0))

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
    except Exception as e:
        app.logger.exception("alien_state error")
        return jsonify({"error": "server", "detail": str(e)}), 500

@app.route("/rl/best_actions", methods=["POST"])
def rl_best_actions():
    data = request.get_json() or {}
    dist = data.get("distribution") or {}   # keys joy/sad/fear/anger/disgust
    if dist:
        state = encode_state({k: float(dist.get(k, 0.0)) for k in ["joy", "sadness", "anger", "fear", "disgust"]})
    else:
        state = (data.get("stateKey") or "").strip()
    acts = Q[state]
    ranked = sorted(acts.items(), key=lambda kv: kv[1], reverse=True)
    return jsonify({"stateKey": state, "rankedIntents": ranked[:3]})

@app.route("/admin/q_table/size", methods=["GET"])
def admin_q_table_size():
    return jsonify({"states": len(Q)})

@app.route("/admin/q_table", methods=["GET"])
def admin_q_table():
    return jsonify(Q)

@app.route("/admin/rl/logs", methods=["GET"])
def admin_rl_logs():
    from datetime import datetime
    d = request.args.get("date") or datetime.utcnow().strftime("%Y%m%d")
    p = LOG_DIR / f"rl_interactions_{d}.jsonl"
    if not p.exists():
        return jsonify({"error": "no_log_for_date"}), 404
    return app.response_class(p.read_text("utf-8"), mimetype="application/jsonl")

@app.route('/health', methods=['GET'])
def health(): 
    return jsonify({"ok": True}), 200

if __name__ == '__main__':
    app.run(port=5000)