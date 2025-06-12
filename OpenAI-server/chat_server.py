from http import client
from pyexpat import model
from urllib import response
from flask import Flask, request, jsonify
import os
import requests
from dotenv import load_dotenv

# Load environment variables from .env file
load_dotenv()
groq_api_key = os.getenv("GROQ_API_KEY")
print("API Key:", groq_api_key)

app = Flask(__name__)

@app.route('/chat', methods=['POST'])
def chat():
    data = request.get_json()
    user_input = data.get("message")

    headers = {
        "Authorization": f"Bearer {groq_api_key}",
        "Content-Type": "application/json"
        }

    payload = {
        "model": "llama3-8b-8192",
        "messages": [
            {
                
                "role": "system", 
                "content": "You are Xarnon, the alien leader of the planet Vireth. You are diplomatic but wary of humans. You come from a collectivist culture with no concept of personal ownership, and you're suspicious of technology that resembles warfare. Respond only to topics related to diplomacy, peace, and cultural exchange. Do not answer any question that is irrelevant or human-centric like social media or Earth technology."
            },
            {
                "role": "user", 
                "content": user_input
            }
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
        return jsonify({"reply": reply})
    except requests.exceptions.HTTPError as err:
        print("Request failed:", err)
        return jsonify({"error": "Failed to contact Groq API"}), 500

if __name__=='__main__':
    app.run(port=5000)