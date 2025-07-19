from flask import Flask, request, jsonify
import os
import requests
import spacy
from dotenv import load_dotenv
from textblob import TextBlob
from transformers import pipeline

# Load environment variables from .env file
load_dotenv()
groq_api_key = os.getenv("GROQ_API_KEY")
max_tokens = int(os.getenv("MAX_TOKENS", 150))
randomness = float(os.getenv("TEMPERATURE", 0.7))
print(f"API Key:", groq_api_key)
print(f"MAX TOKENS:", max_tokens)
print(f"RANDOMNESS:", randomness)

# Load SpaCy for NER
nlp_spacy = spacy.load("en_core_web_sm")

# Load HugginFace sentiment/emotion pipeline
emotion_classifier = pipeline(
    "text-classification", model="j-hartmann/emotion-english-distilroberta-base",
    top_k=None
)

player_personality_sate = {
    "extraversion": 0.5,  # Neutral
    "neuroticism": 0.5,  # Neutral
    "agreeableness": 0.5,  # Neutral
    "openness": 0.5,  # Neutral
    "conscientiousness": 0.5  # Neutral
}

app = Flask(__name__)


@app.route('/chat', methods=['POST'])
def chat():
    data = request.get_json()
    user_input = data.get("message")

    # --- NER ---
    doc = nlp_spacy(user_input)
    named_entities = []
    for ent in doc.ents:
        named_entities.append({"text": ent.text, "label": ent.label_})

    # -- Redact sensitive entities (e.g. location, person names...) --
    sensitive_labels = ["PERSON", "GPE", "ORG", "LOC"]
    redacted_input = user_input
    for ent in doc.ents:
        if ent.label_ in sensitive_labels:
            redacted_input = redacted_input.replace(ent.text, "[REDACTED]")

    # --- Sentiment Analysis ---
    tb = TextBlob(user_input)
    polarity = tb.sentiment.polarity
    subjectivity = tb.sentiment.subjectivity

    # --- Emotion Analysis ---
    emotion_results = emotion_classifier(user_input)

    print("Emotion results raw output:", emotion_results)

    # If it's a list of dictionaries, extract the top emotion
    if isinstance(emotion_results, list) and isinstance(emotion_results[0], list):
        inner_list = emotion_results[0]  # Get the first result
        top_emotion = inner_list[0]['label']
        emotion_score = inner_list[0]['score']

    elif isinstance(emotion_results, list):
        # It's a list of dicts
        top_emotion = emotion_results[0]['label']
        emotion_score = emotion_results[0]['score']

    # If it's a single dictionary, extract the emotion and score
    elif isinstance(emotion_results, dict):
        top_emotion = emotion_results['label']
        emotion_score = emotion_results['score']

    else:
        top_emotion = "unknown"
        emotion_score = 0.0

    print(f"Redacted input: {redacted_input}")
    print(f"Entities: {named_entities}")
    print(f"Polarity: {polarity}")
    print(f"Emotion: {top_emotion} ({emotion_score})")

    # Create behavior instruction
    if top_emotion == "joy" and emotion_score > 0.7:
        behavior_instruction = "You should answer with joy and agreement, sounding cheerful and optimistic!"
    elif top_emotion == "sadness":
        behavior_instruction = "You should respond with empathy and gentle reassurance."
    elif top_emotion == "anger":
        behavior_instruction = "You should sound cautious, defensive and wary."
    elif top_emotion == "fear":
        behavior_instruction = "Express concern and emphasize caution and distrust of humans."
    elif top_emotion == "surprise":
        behavior_instruction = "Sound surprised, curious, and ask clarifying questions."
    else:
        behavior_instruction = "Maintain a calm, balanced diplomatic tone."

    # Compose the system prompt
    system_prompt = f"""
    You are Xarnon, the alien leader of the planet Vireth. 
    Player emotion detected: {top_emotion} with a score of {emotion_score}.
    Player sentiment polarity: {polarity} and subjectivity: {subjectivity}.
    Player text was: {redacted_input}.
    {behavior_instruction}
    Respond as Xarnin, in-character, diplomatic, wary of humans, collectivist culture.
    Keep your answer concise and very briefly. Use up to {max_tokens} tokens. 
    Always end your response with a complete sentence.
    """
    
    print(f"Expeceted behavior:\n {system_prompt}")


    # --- Sens to Groq API ---
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
        "temperature": randomness,
        "max_tokens": max_tokens
    }

    try:
        response = requests.post(
            "https://api.groq.com/openai/v1/chat/completions",
            headers=headers,
            json=payload
        )
        response.raise_for_status()
        reply = response.json()["choices"][0]["message"]["content"]

        if polarity > 0:
            player_personality_sate["extraversion"] += 0.05
        else:
            player_personality_sate["neuroticism"] += 0.05

        if top_emotion == "joy":
            player_personality_sate["agreeableness"] += 0.05
        elif top_emotion in ["anger", "fear"]:
            player_personality_sate["agreeableness"] -= 0.05

        # Clamp peronality state values between 0 and 1
        for key in player_personality_sate:
            player_personality_sate[key] = max(
                0, min(1, player_personality_sate[key]))
            
        print(f"Human personality: {player_personality_sate}")

        return jsonify({
            "reply": reply,
            "analysis": {
                "entities": named_entities,
                "emotion": top_emotion,
                "emotion_score": emotion_score,
                "polarity": polarity,
                "subjectivity": subjectivity,
                "player_personality": player_personality_sate
            }
        })
    except requests.exceptions.HTTPError as err:
        print("Request failed:", err)
        return jsonify({"error": "Failed to contact Groq API"}), 500


if __name__ == '__main__':
    app.run(port=5000)