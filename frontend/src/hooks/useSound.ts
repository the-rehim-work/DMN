type SoundName = 'tile_play' | 'tile_draw' | 'domino' | 'round_end' | 'your_turn' | 'double_play' | 'special_play';

const SOUNDS: Record<SoundName, { frequency: number; duration: number; type: OscillatorType; pattern?: number[] }> = {
  tile_play: { frequency: 400, duration: 100, type: 'square' },
  tile_draw: { frequency: 300, duration: 80, type: 'sine' },
  domino: { frequency: 600, duration: 300, type: 'triangle', pattern: [600, 800, 1000] },
  round_end: { frequency: 500, duration: 200, type: 'sine', pattern: [500, 400, 300] },
  your_turn: { frequency: 700, duration: 150, type: 'sine', pattern: [700, 900] },
  double_play: { frequency: 500, duration: 150, type: 'square', pattern: [500, 700] },
  special_play: { frequency: 800, duration: 200, type: 'custom', pattern: [] },
};

let audioContext: AudioContext | null = null;

function getAudioContext(): AudioContext {
  if (!audioContext) {
    audioContext = new AudioContext();
  }
  return audioContext;
}

function playTone(frequency: number, duration: number, type: OscillatorType, volume = 0.3) {
  const ctx = getAudioContext();
  const oscillator = ctx.createOscillator();
  const gainNode = ctx.createGain();

  oscillator.type = type;
  oscillator.frequency.setValueAtTime(frequency, ctx.currentTime);

  gainNode.gain.setValueAtTime(volume, ctx.currentTime);
  gainNode.gain.exponentialRampToValueAtTime(0.01, ctx.currentTime + duration / 1000);

  oscillator.connect(gainNode);
  gainNode.connect(ctx.destination);

  oscillator.start(ctx.currentTime);
  oscillator.stop(ctx.currentTime + duration / 1000);
}

function playSynthSound(name: SoundName, volume = 0.3) {
  const sound = SOUNDS[name];
  if (!sound) return;

  if (sound.pattern) {
    sound.pattern.forEach((freq, i) => {
      setTimeout(() => playTone(freq, sound.duration, sound.type, volume), i * sound.duration);
    });
  } else {
    playTone(sound.frequency, sound.duration, sound.type, volume);
  }
}

const fileCache: Record<string, HTMLAudioElement> = {};

function playFileSound(path: string, volume = 0.5) {
  let audio = fileCache[path];
  if (!audio) {
    audio = new Audio(path);
    fileCache[path] = audio;
  }
  audio.volume = volume;
  audio.currentTime = 0;
  audio.play().catch(() => {});
}

export function useSound() {
  const playSound = (name: SoundName, options?: { file?: string; volume?: number }) => {
    const muted = localStorage.getItem('game_muted') === 'true';
    if (muted) return;

    if (options?.file) {
      playFileSound(options.file, options.volume ?? 0.5);
    } else {
      playSynthSound(name, options?.volume ?? 0.3);
    }
  };

  const isMuted = () => localStorage.getItem('game_muted') === 'true';

  const setMuted = (muted: boolean) => {
    localStorage.setItem('game_muted', String(muted));
  };

  const toggleMute = () => {
    const current = isMuted();
    setMuted(!current);
    return !current;
  };

  return { playSound, isMuted, setMuted, toggleMute };
}

export type { SoundName };