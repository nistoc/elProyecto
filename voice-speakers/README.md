# voice-speakers

Who speaks when in a lesson recording, told by **voice** on the local GPU — a free source of speakers for a recording
transcribed without them (the plain model `gpt-transcribe` writes no speakers; the diarize model gives them but costs
about $0.027 a minute of recording, three times the text itself).

It writes no text, only times and voice labels: `S1`, `S2`, … by their total time.

## How it works

`diarize.py`, in a container on the GPU (it never falls back to the CPU, exit 3 without one):

1. the audio is read as 16 kHz mono (ffmpeg);
2. windows of 1.5 s every 0.5 s (`VOICE_WIN_SEC`, `VOICE_HOP_SEC`); a window more than 30 dB below the loud windows is
   taken as silence; the voice prints are made 64 windows at a time, halved when the GPU has no memory for them;
3. every window gets a voice print — ECAPA-TDNN, `speechbrain/spkrec-ecapa-voxceleb` (Apache 2.0, not gated; it is
   downloaded on the first run into the Docker volume `voice-speakers-models`);
4. the prints are clustered: cosine affinity, the strongest 10 % neighbours of every window kept, spectral
   clustering, the number of voices by the largest eigengap (2–4 by default), one pass back to the nearest voice centre;
5. every 0.25 s frame takes the voice of the windows that cover it (the window centre weighs most); runs of one voice
   become segments.

## Run

```bash
docker build -t voice-speakers:0.1 voice-speakers

# Git Bash: MSYS_NO_PATHCONV=1 keeps the container paths as they are
MSYS_NO_PATHCONV=1 docker run --rm --gpus all \
  -v <jobs folder>:/jobs:ro -v <out folder>:/out -v voice-speakers-models:/models \
  voice-speakers:0.1 /jobs/<job id>/<audio file> /out/<job id>.json [--min-speakers 2] [--max-speakers 4]
```

The answer (`<job id>.json`): `duration_sec`, `speakers`, `eigenvalues`, `windows`, `windows_kept`,
`seconds_by_speaker`, `timing_sec`, `segments` — `[{start, end, speaker}]` in seconds of the audio file.
Keep it next to the jobs folder, not inside it (every folder there is a job): agent05 reads
`<VoiceSpeakers:RootPath>/<job id>.json` (by default the folder `voice-speakers` next to the jobs folder) for the job
whose audio it read, see "Lesson text with speakers" in the root `CLAUDE.md`.

## Measured (2026-11-20)

- Speed: a 40-minute recording in 13–29 s on an RTX 5070 Laptop GPU (reading the audio 2–10 s, voice prints 3–16 s,
  clusters under 5 s), the model loaded once per run.
- Right person, by words of our transcript, on a recording that also has a track per participant (the tracks are the
  truth): 94.8 % with the voice alone; 97.7 % when the voice names the speakers of a diarize run of the same file
  (`DiarizedSpeakers.TieByVoice`), the same as the tie of that run by shared words.
- Agreement with a diarize run on three other mixed recordings: 93.1 %, 95.0 %, 90.8 %. Where they disagree most, the
  diarize model had named the teacher anew inside a chunk and the tie by shared words carried that into the last
  6 minutes; the voice did not.
- Windows tried: 1.0, 1.5 and 2.0 s every 0.5 s, 1.5 s every 0.75 s, 2.0 s every 1.0 s — 93.6–94.8 % with the voice
  alone, 1.5 s every 0.5 s the best; the voice-named diarize run stayed at 97.7 % with every one. The number of voices
  came out 2 on every lesson recording tried.
