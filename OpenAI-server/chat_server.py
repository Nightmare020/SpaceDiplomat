from http import client
from pyexpat import model
from urllib import response
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
print("API Key:", groq_api_key)

# Load SpaCy for NER
nlp_spacy = spacy.load("en_core_web_sm")

# Load HugginFace sentiment/emotion pipeline
emotion_classifier = pipeline(
    "text-classification", model="j-hartmann/emotion-english-distilroberta-base",
    top_k=None
)

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

    # Compose the system prompt
    system_prompt = f"""
    You are Xarnon, the alien leader of the planet Vireth. 
    Player emotion detected: {top_emotion} with a score of {emotion_score}.
    Player sentiment polarity: {polarity} and subjectivity: {subjectivity}.
    Player text was: {redacted_input}.
    Respond as Xarnin, in-character, diplomatic, wary of humans, collectivist culture.
    """

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
        "temperature": 0.7,
        "max_tokens": 150
    }

    try:
        response = requests.post(
            "https://api.groq.com/openai/v1/chat/completions",
            headers=headers,
            json=payload
        )
        response.raise_for_status()
        reply = response.json()["choices"][0]["message"]["content"]

        return jsonify({
            "reply": reply,
            "analysis": {
                "entities": named_entities,
                "emotion": top_emotion,
                "emotion_score": emotion_score,
                "polarity": polarity,
                "subjectivity": subjectivity
            }
        })
    except requests.exceptions.HTTPError as err:
        print("Request failed:", err)
        return jsonify({"error": "Failed to contact Groq API"}), 500


if __name__ == '__main__':
    app.run(port=5000)