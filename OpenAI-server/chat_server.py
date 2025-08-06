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
    elif top_emotion == "surprise":
        return "Sound surprised, curious, and ask clarifying questions."
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
    canon_order = ["surprise", "fear", "anger", "sadness", "joy"]
    raw = {d['label'].lower(): float(d['score']) for d in emotion_results[0]}
    vals = [raw.get(k, 0.0) for k in canon_order]
    s = sum(vals) or 1.0
    vals = [v / s for v in vals]

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
    Keep your answer concise and very briefly. Use up to {max_tokens} tokens. 
    Always end your response with a complete sentence.
    """.strip()
    
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
        "messages": [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": redacted_input}
        ],
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
    traits = profile.get("traits", {})
    ag = float(traits.get("agreeableness", 0.5))

    # Personality-biased gains
    anger_gain = emotion_score * (1.0 + 0.6 * na - 0.4 * ag) if top_emotion == "anger" else 0.0
    joy_gain = emotion_score * (1.0 + 0.4 * ag - 0.1 * na) if top_emotion == "joy" else 0.0

    # Passive decay towards 0 (feelings fade)
    alien_affect[alien_name]["anger"] = max(0.0, alien_affect[alien_name]["anger"] * 0.85 + anger_gain)
    alien_affect[alien_name]["joy"] = max(0.0, alien_affect[alien_name]["joy"] * 0.85 + joy_gain)

    # Gates
    joy_threshold = float(profile.get("joyThreshold", 0.9))
    anger_tolerance = float(profile.get("angerTolerance", 0.3))
    success = alien_affect[alien_name]["joy"] >= joy_threshold
    failure = alien_affect[alien_name]["anger"] >= anger_tolerance

    # Log current state
    print(f"[{alien_name}] joy={alien_affect[alien_name]['joy']:.3f} / thr={joy_threshold} | "
          f"anger={alien_affect[alien_name]['anger']:.3f} / tol={anger_tolerance} | "
          f"success={success} failure={failure}")

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
        "negotiationFailure": failure
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