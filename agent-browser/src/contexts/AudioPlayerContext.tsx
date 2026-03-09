import React, { createContext, useContext, useState, useRef, useEffect, useCallback, ReactNode } from "react";

interface AudioPlayerContextType {
  currentUrl: string | null;
  currentTitle: string | null;
  isPlaying: boolean;
  duration: number;
  currentTime: number;
  play: (url: string, title?: string) => void;
  pause: () => void;
  stop: () => void;
  toggle: () => void;
  seek: (timeInSeconds: number) => void;
}

const AudioPlayerContext = createContext<AudioPlayerContextType | undefined>(undefined);

export function AudioPlayerProvider({ children }: { children: ReactNode }) {
  const [currentUrl, setCurrentUrl] = useState<string | null>(null);
  const [currentTitle, setCurrentTitle] = useState<string | null>(null);
  const [isPlaying, setIsPlaying] = useState(false);
  const [duration, setDuration] = useState(0);
  const [currentTime, setCurrentTime] = useState(0);
  const audioRef = useRef<HTMLAudioElement | null>(null);

  const play = (url: string, title?: string) => {
    if (currentUrl === url && audioRef.current) {
      // Same file - toggle play/pause
      if (audioRef.current.paused) {
        audioRef.current.play();
        setIsPlaying(true);
      } else {
        audioRef.current.pause();
        setIsPlaying(false);
      }
    } else {
      // New file - start playing
      setCurrentUrl(url);
      setCurrentTitle(title || null);
      if (audioRef.current) {
        audioRef.current.src = url;
        audioRef.current.play();
        setIsPlaying(true);
      }
    }
  };

  const pause = () => {
    if (audioRef.current) {
      audioRef.current.pause();
      setIsPlaying(false);
    }
  };

  const stop = () => {
    if (audioRef.current) {
      audioRef.current.pause();
      audioRef.current.currentTime = 0;
      setIsPlaying(false);
    }
    setCurrentUrl(null);
    setCurrentTitle(null);
    setDuration(0);
    setCurrentTime(0);
  };

  const seek = useCallback((timeInSeconds: number) => {
    if (audioRef.current) {
      const t = Math.max(0, timeInSeconds);
      audioRef.current.currentTime = t;
      setCurrentTime(t);
    }
  }, []);

  const toggle = () => {
    if (audioRef.current) {
      if (audioRef.current.paused) {
        audioRef.current.play();
        setIsPlaying(true);
      } else {
        audioRef.current.pause();
        setIsPlaying(false);
      }
    }
  };

  // Handle audio events (ref is stable; attach once and sync from element when src changes)
  useEffect(() => {
    const audio = audioRef.current;
    if (!audio) return;

    const handlePlay = () => setIsPlaying(true);
    const handlePause = () => setIsPlaying(false);
    const handleEnded = () => {
      setIsPlaying(false);
      setCurrentUrl(null);
      setCurrentTitle(null);
      setDuration(0);
      setCurrentTime(0);
    };
    const handleLoadedMetadata = () => {
      const d = audio.duration;
      setDuration(Number.isFinite(d) ? d : 0);
      setCurrentTime(audio.currentTime);
    };
    const handleTimeUpdate = () => setCurrentTime(audio.currentTime);

    audio.addEventListener("play", handlePlay);
    audio.addEventListener("pause", handlePause);
    audio.addEventListener("ended", handleEnded);
    audio.addEventListener("loadedmetadata", handleLoadedMetadata);
    audio.addEventListener("timeupdate", handleTimeUpdate);
    // Sync once in case element already had src
    if (audio.src) {
      handleLoadedMetadata();
      handleTimeUpdate();
    }

    return () => {
      audio.removeEventListener("play", handlePlay);
      audio.removeEventListener("pause", handlePause);
      audio.removeEventListener("ended", handleEnded);
      audio.removeEventListener("loadedmetadata", handleLoadedMetadata);
      audio.removeEventListener("timeupdate", handleTimeUpdate);
    };
  }, []);

  return (
    <AudioPlayerContext.Provider
      value={{
        currentUrl,
        currentTitle,
        isPlaying,
        duration,
        currentTime,
        play,
        pause,
        stop,
        toggle,
        seek,
      }}
    >
      {children}
      <audio ref={audioRef} style={{ display: "none" }} />
    </AudioPlayerContext.Provider>
  );
}

export function useAudioPlayer() {
  const context = useContext(AudioPlayerContext);
  if (context === undefined) {
    throw new Error("useAudioPlayer must be used within an AudioPlayerProvider");
  }
  return context;
}
