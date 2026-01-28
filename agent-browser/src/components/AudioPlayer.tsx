import React from "react";
import { useAudioPlayer } from "../contexts/AudioPlayerContext";

export function AudioPlayer() {
  const { currentUrl, currentTitle, isPlaying, pause, stop, toggle } = useAudioPlayer();

  if (!currentUrl) {
    return null;
  }

  return (
    <div className="audio-player">
      <div className="audio-player__content">
        <div className="audio-player__info">
          <span className="audio-player__title">
            {currentTitle || "Аудио файл"}
          </span>
        </div>
        <div className="audio-player__controls">
          <button
            className="audio-player__btn"
            onClick={toggle}
            title={isPlaying ? "Пауза" : "Воспроизвести"}
          >
            {isPlaying ? "⏸" : "▶"}
          </button>
          <button
            className="audio-player__btn"
            onClick={stop}
            title="Остановить"
          >
            ⏹
          </button>
        </div>
      </div>
    </div>
  );
}
