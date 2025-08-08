import dis
import time
from flask import Flask, request, jsonify
import os
import requests
import spacy
import json
from dotenv import load_dotenv
from textblob import TextBlob
from transformers import pipeline
from collections import defaultdict

# ======================================
# Bootstrap / Configuration
# ======================================

# Load environment variables from .env file
load_dotenv()

groq_api_key = os.getenv("GROQ_API_KEY")
max_tokens = int(os.getenv("MAX_TOKENS", 150))
base_temperature = float(os.getenv("TEMPERATURE", 0.7))
alien_profiles_path = os.getenv("ALIEN_PROFILES_PATH", "AlienPersonalities.json")

print(f"API KEY:", groq_api_key)
print(f"MAX TOKENS:", max_tokens)
print(f"TEMPERATURE:", base_temperature)
print(f"ALIEN PROFILES PATH:", alien_profiles_path)

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
Q = defaultdict(lambda: {a: 0.0 for a in INTENTS})
EPSILON = 0.15
ALPHA = 0.25
GAMMA = 0.90

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

ALIENS = load_alien_profiles(alien_profiles_path)

# Running affect per alien (possibly swapped for FAtiMa later)
alien_affect = defaultdict(lambda: {"joy": 0.0, "anger": 0.0})

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
    if "ner" not in nlp_spacy.pipe_names:
        print("Warning: NER is not available. Redaction will not work.")
        return text, []

    doc = nlp_spacy(text)
    named = [{"text": ent.text, "label": ent.label_} for ent in doc.ents]
    sensitive_labels = ["PERSON", "GPE", "ORG", "LOC"]
    redacted = text
    for ent in doc.ents:
        if ent.label_ in sensitive_labels:
            redacted = redacted.replace(ent.text, "[REDACTED]")
    return redacted, named

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

    if not user_input:
        return jsonify({"error": "Empty message"}), 400

    profile = ALIENS.get(alien_name)
    if profile is None:
        return jsonify({"error": f"Alien profile '{alien_name}' not found"}), 400

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

    distribution_json = json.dumps({
        "keys": canon_order,
        "values": vals
    })

    # --- Persona & style ---
    style_hints, eo, na, c = style_hints_from_traits(profile.get("traits", {}))
    behavior_instruction = behavior_from_emotion(top_emotion, emotion_score)
    temp = adjusted_temperature(base_temperature, c)

    # --- System prompt ---
    system_prompt = f"""
    You are {profile['name']}, the alien leader.
    Persona: {profile.get('personalityType','')}.
    Description: {profile.get('description', '')}.
    Culture: {profile.get('culture', '')}.
    Stay in-character. {profile.get('behaviorInstruction', '')}
    Style hints: {", ".join(style_hints)}.

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

    # Log current state
    print(f"[{alien_name}] joy={alien_affect[alien_name]['joy']:.3f} / thr={joy_threshold} | "
          f"anger={alien_affect[alien_name]['anger']:.3f} / tol={anger_tolerance} | "
          f"success={success} failure={failure}")

    rl_reward = compute_reward(dist_map)
    Q_prev = Q[rl_state][rl_action]
    Q[rl_state][rl_action] = Q_prev + ALPHA * (rl_reward - Q_prev)

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
    alien_name = data.get("alienName", None)
    if alien_name and alien_name in alien_affect:
        alien_affect[alien_name] = {"joy": 0.0, "anger": 0.0}
        return jsonify({"ok": True, "reset": alien_name})
    elif not alien_name:
        # Reset all aliens
        alien_affect.clear()
        return jsonify({"ok": True, "reset": "ALL"})
    return jsonify({"ok": False, "error": "Unknown alien"}), 400

if __name__ == '__main__':
    app.run(port=5000)