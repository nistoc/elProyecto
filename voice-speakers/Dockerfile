# Speakers of a recording told by voice, on the GPU (see README.md).
FROM pytorch/pytorch:2.8.0-cuda12.8-cudnn9-runtime

RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*
RUN pip install --no-cache-dir speechbrain==1.0.3 huggingface_hub==0.25.2

ENV HF_HOME=/models/hf VOICE_MODEL_DIR=/models/ecapa PYTHONUNBUFFERED=1
WORKDIR /app
COPY diarize.py /app/diarize.py
ENTRYPOINT ["python", "/app/diarize.py"]
