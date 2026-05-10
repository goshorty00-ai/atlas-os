import { useEffect, useMemo, useRef, useState } from "react";

// ── AI bridge ────────────────────────────────────────────────────────────────
function postToHost(type: string, payload: object): void {
  try {
    const wv = (window as any)?.chrome?.webview;
    if (wv?.postMessage) wv.postMessage({ type, payload });
  } catch {}
}

// ── Types ─────────────────────────────────────────────────────────────────────
interface Question {
  id: string;
  round: number;
  category: string;
  difficulty: string;
  question: string;
  options: string[];
  correctAnswer: number;
  explanation: string;
  points: number;
  timeLimit: number;
}

interface QuizGenerateResult {
  ok: boolean;
  questions?: unknown[];
  source?: string;
  error?: string;
}

interface QuizSpeakResult {
  ok: boolean;
  requestId?: string;
  error?: string;
}

interface Player {
  id: string;
  name: string;
}

const AI_QUIZ_PLAYERS_STORAGE_KEY = "atlas.aiQuiz.players.v1";

const DEFAULT_PLAYERS: Player[] = [
  { id: "player-alice", name: "Alice" },
  { id: "player-bob", name: "Bob" },
];

function createPlayerId(name: string): string {
  return `player-${name.toLowerCase().replace(/[^a-z0-9]+/g, "-")}-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 6)}`;
}

function sanitizeName(name: unknown): string {
  return String(name ?? "").trim();
}

function parseStoredPlayers(raw: string | null): Player[] | null {
  if (!raw) return null;

  try {
    const parsed = JSON.parse(raw);
    if (!Array.isArray(parsed)) return null;

    const fromLegacyStrings = parsed.every((item) => typeof item === "string");
    const items = fromLegacyStrings
      ? parsed.map((name, index) => {
          const clean = sanitizeName(name);
          const slug = clean.toLowerCase().replace(/[^a-z0-9]+/g, "-") || `legacy-${index + 1}`;
          return { id: `player-${slug}`, name: clean } as Player;
        })
      : parsed;

    const normalized = items
      .map((item) => ({
        id: sanitizeName((item as Player)?.id),
        name: sanitizeName((item as Player)?.name),
      }))
      .filter((item) => item.id.length > 0 && item.name.length > 0);

    if (normalized.length === 0) return null;

    const uniqueById = new Map<string, Player>();
    for (const player of normalized) {
      if (!uniqueById.has(player.id)) {
        uniqueById.set(player.id, player);
      }
    }

    const uniquePlayers = [...uniqueById.values()];
    return uniquePlayers.length > 0 ? uniquePlayers : null;
  } catch {
    return null;
  }
}

function validatePlayers(input: unknown): Player[] {
  if (!Array.isArray(input)) return [];

  const normalized = input
    .map((item, index) => {
      if (typeof item === "string") {
        const legacyName = sanitizeName(item);
        const legacySlug = legacyName.toLowerCase().replace(/[^a-z0-9]+/g, "-") || `legacy-${index + 1}`;
        return { id: `player-${legacySlug}`, name: legacyName } as Player;
      }

      const player = item as Partial<Player>;
      const name = sanitizeName(player?.name);
      const fallbackId = name
        ? `player-${name.toLowerCase().replace(/[^a-z0-9]+/g, "-") || `legacy-${index + 1}`}`
        : "";
      const id = sanitizeName(player?.id) || fallbackId;
      return { id, name };
    })
    .filter((item) => item.id.length > 0 && item.name.length > 0);

  if (normalized.length === 0) return [];

  const uniqueById = new Map<string, Player>();
  for (const player of normalized) {
    if (!uniqueById.has(player.id)) {
      uniqueById.set(player.id, player);
    }
  }

  return [...uniqueById.values()];
}

type PlayersLoadResult = {
  players: Player[];
  source: "saved" | "default" | "memory";
  warning: string;
};

function canUseLocalStorage(): boolean {
  try {
    const key = "atlas.aiQuiz.players.storage.check";
    window.localStorage.setItem(key, "1");
    window.localStorage.removeItem(key);
    return true;
  } catch {
    return false;
  }
}

function loadPlayers(): PlayersLoadResult {
  if (!canUseLocalStorage()) {
    const players = [...DEFAULT_PLAYERS];
    console.log(`[AIQuiz] players.load source=memory count=${players.length}`);
    return {
      players,
      source: "memory",
      warning: "Player persistence unavailable on this device; using in-memory players for this session.",
    };
  }

  try {
    const stored = parseStoredPlayers(window.localStorage.getItem(AI_QUIZ_PLAYERS_STORAGE_KEY));
    const validated = validatePlayers(stored ?? []);
    if (validated.length > 0) {
      console.log(`[AIQuiz] players.load source=saved count=${validated.length}`);
      return { players: validated, source: "saved", warning: "" };
    }
  } catch {}

  const players = [...DEFAULT_PLAYERS];
  console.log(`[AIQuiz] players.load source=default count=${players.length}`);
  return { players, source: "default", warning: "" };
}

function savePlayers(players: Player[]): { ok: boolean; source: "saved" | "memory"; warning: string } {
  if (!canUseLocalStorage()) {
    console.log(`[AIQuiz] players.save count=${players.length}`);
    return {
      ok: false,
      source: "memory",
      warning: "Player persistence unavailable on this device; using in-memory players for this session.",
    };
  }

  try {
    window.localStorage.setItem(AI_QUIZ_PLAYERS_STORAGE_KEY, JSON.stringify(players));
    console.log(`[AIQuiz] players.save count=${players.length}`);
    return { ok: true, source: "saved", warning: "" };
  } catch {
    console.log(`[AIQuiz] players.save count=${players.length}`);
    return {
      ok: false,
      source: "memory",
      warning: "Player persistence unavailable on this device; using in-memory players for this session.",
    };
  }
}

function buildZeroScores(players: Player[]): Record<string, number> {
  const next: Record<string, number> = {};
  players.forEach((player) => {
    next[player.id] = 0;
  });
  return next;
}

const SOUND_HOOKS = {
  reveal: "",
  correct: "",
  wrong: "",
  score: "",
  next: "",
  finish: "",
} as const;

type SoundHookName = keyof typeof SOUND_HOOKS;

function playSoundHook(name: SoundHookName): void {
  const src = SOUND_HOOKS[name];
  if (!src) return;

  try {
    const audio = new Audio(src);
    audio.volume = 0.5;
    void audio.play().catch(() => {});
  } catch {}
}

function normalizeQuestionText(text: string): string {
  return text
    .toLowerCase()
    .replace(/[^a-z0-9\s]/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

function tokenSet(text: string): Set<string> {
  const normalized = normalizeQuestionText(text);
  if (!normalized) return new Set();
  return new Set(normalized.split(" ").filter((token) => token.length > 2));
}

function jaccardSimilarity(a: Set<string>, b: Set<string>): number {
  if (a.size === 0 || b.size === 0) return 0;
  let intersection = 0;
  for (const token of a) {
    if (b.has(token)) intersection++;
  }
  const union = a.size + b.size - intersection;
  return union === 0 ? 0 : intersection / union;
}

function hasDuplicateHeavyQuestions(questions: Question[]): boolean {
  const normalizedSeen: string[] = [];
  const tokenSets: Set<string>[] = [];

  for (const question of questions) {
    const normalized = normalizeQuestionText(question.question);
    if (!normalized) continue;

    if (normalizedSeen.includes(normalized)) {
      return true;
    }

    const currentTokens = tokenSet(question.question);
    for (const tokens of tokenSets) {
      if (jaccardSimilarity(currentTokens, tokens) >= 0.82) {
        return true;
      }
    }

    normalizedSeen.push(normalized);
    tokenSets.push(currentTokens);
  }

  return false;
}

function sanitizeHostError(message: unknown): string {
  const text = String(message ?? "").trim();
  if (!text) {
    return "AI quiz generation failed. Try again or adjust the quiz settings.";
  }

  const allowed = new Set([
    "AI returned malformed quiz JSON. Try again or adjust the quiz settings.",
    "AI generated quiz data failed validation. Try again or adjust settings.",
    "AI quiz generation failed. Try again or adjust the quiz settings.",
    "AI provider is unavailable right now. Try again in a moment.",
    "AI returned an empty response.",
    "Atlas AI bridge is only available inside Atlas.",
  ]);

  return allowed.has(text)
    ? text
    : "AI quiz generation failed. Try again or adjust the quiz settings.";
}

function clampQuestionCount(count: number): number {
  if (!Number.isFinite(count)) return 10;
  return Math.max(1, Math.min(50, Math.trunc(count)));
}

function speakViaBrowser(text: string): Promise<boolean> {
  return new Promise((resolve) => {
    const synth = (window as Window & { speechSynthesis?: SpeechSynthesis }).speechSynthesis;
    const Ctor = (window as Window & typeof globalThis).SpeechSynthesisUtterance;
    if (!synth || !Ctor) {
      resolve(false);
      return;
    }

    const utterance = new Ctor(text);
    utterance.rate = 1;
    utterance.pitch = 1;
    utterance.volume = 1;
    utterance.onend = () => resolve(true);
    utterance.onerror = () => resolve(false);

    try {
      synth.cancel();
      synth.speak(utterance);
    } catch {
      resolve(false);
    }
  });
}

function stopBrowserSpeech(): void {
  try {
    const synth = (window as Window & { speechSynthesis?: SpeechSynthesis }).speechSynthesis;
    synth?.cancel();
  } catch {}
}

function buildScoreGuardKey(questionIndex: number, playerId: string): string {
  return `${questionIndex}:${playerId}`;
}

// ── Question parser (client-side safety net) ──────────────────────────────────
function parseIncomingQuestion(input: unknown, index: number): Question | null {
  if (!input || typeof input !== "object") return null;
  const item = input as Record<string, unknown>;
  const question = String(item.question ?? "").trim();
  if (!question) return null;
  const optionList = Array.isArray(item.options)
    ? item.options.map((v) => String(v ?? "").trim()).filter((v) => v.length > 0)
    : [];
  if (optionList.length === 0) return null;
  let correctAnswer = Number(item.correctAnswer);
  if (!Number.isInteger(correctAnswer) || correctAnswer < 0 || correctAnswer >= optionList.length) {
    const asText = String(item.correctAnswer ?? "").trim();
    const found = optionList.findIndex((o) => o.toLowerCase() === asText.toLowerCase());
    correctAnswer = found >= 0 ? found : 0;
  }
  return {
    id: String(item.id ?? `ai-q-${index + 1}`),
    round: Math.max(1, Number(item.round) || 1),
    category: String(item.category ?? "General"),
    difficulty: String(item.difficulty ?? "Mixed"),
    question,
    options: optionList,
    correctAnswer,
    explanation: String(item.explanation ?? ""),
    points: Math.max(10, Number(item.points) || 100),
    timeLimit: Math.max(10, Number(item.timeLimit) || 30),
  };
}

// ── Sidebar modes ─────────────────────────────────────────────────────────────
const MODES = [
  { id: "classic", label: "Classic Quiz",    icon: "⊙" },
  { id: "movie",   label: "Movie Night",     icon: "⊞" },
  { id: "music",   label: "Music Round",     icon: "♪" },
  { id: "picture", label: "Picture Round",   icon: "⊡" },
  { id: "family",  label: "Family Mode",     icon: "⊙" },
  { id: "pub",     label: "Pub Quiz",        icon: "⊙" },
  { id: "speed",   label: "Speed Round",     icon: "⚡" },
  { id: "custom",  label: "Custom AI Round", icon: "✦" },
  { id: "leader",  label: "Leaderboards",    icon: "⊙" },
];

const DIFFICULTIES = ["Easy", "Medium", "Hard", "Brutal"];

// ── Shared card styles ────────────────────────────────────────────────────────
const CARD: React.CSSProperties = {
  background: "rgba(255,255,255,0.025)",
  border: "1px solid rgba(255,255,255,0.07)",
  borderRadius: 18,
  padding: 22,
  display: "flex",
  flexDirection: "column",
  gap: 16,
};

// ── App ───────────────────────────────────────────────────────────────────────
export default function App() {
  const playersBootstrap = useMemo(() => loadPlayers(), []);

  // Sidebar / mode
  const [selectedMode, setSelectedMode] = useState("classic");

  // Players
  const [players, setPlayers] = useState<Player[]>(playersBootstrap.players);
  const [scores, setScores] = useState<Record<string, number>>(buildZeroScores(playersBootstrap.players));
  const [showAddPlayer, setShowAddPlayer] = useState(false);
  const [newPlayerName, setNewPlayerName] = useState("");
  const [playerStatus, setPlayerStatus] = useState("");
  const [playerStorageWarning, setPlayerStorageWarning] = useState(playersBootstrap.warning);
  const [currentPlayerId, setCurrentPlayerId] = useState(playersBootstrap.players[0]?.id ?? "");
  const [lastScoredPlayerId, setLastScoredPlayerId] = useState<string | null>(null);

  // Game state
  const [gameStatus, setGameStatus] = useState<"ready" | "running" | "paused" | "finished">("ready");
  const [activeQuestions, setActiveQuestions] = useState<Question[]>([]);
  const [questionIndex, setQuestionIndex] = useState(0);
  const [answerRevealed, setAnswerRevealed] = useState(false);
  const [selectedAnswerIndex, setSelectedAnswerIndex] = useState<number | null>(null);
  const [scoredThisQuestion, setScoredThisQuestion] = useState<Set<string>>(new Set());
  const [scoreStatus, setScoreStatus] = useState("");
  const [questionAnimTick, setQuestionAnimTick] = useState(0);
  const [selectionPulseIndex, setSelectionPulseIndex] = useState<number | null>(null);
  const [answerOutcome, setAnswerOutcome] = useState<"none" | "correct" | "wrong">("none");
  const [timeRemaining, setTimeRemaining] = useState(0);
  const [timeUp, setTimeUp] = useState(false);
  const [totalRounds, setTotalRounds] = useState(5);
  const [questionCount, setQuestionCount] = useState(10);

  // Generator state
  const [prompt, setPrompt] = useState("");
  const [difficulty, setDifficulty] = useState("Hard");
  const [isGenerating, setIsGenerating] = useState(false);
  const [generationError, setGenerationError] = useState("");
  const [generationSource, setGenerationSource] = useState("");
  const [generatedCount, setGeneratedCount] = useState(0);
  const [voiceStatus, setVoiceStatus] = useState("");
  const [voiceMuted] = useState(false);
  const [isSpeaking, setIsSpeaking] = useState(false);

  const hasWebViewBridge = Boolean((window as any)?.chrome?.webview?.postMessage);
  const pendingSpeakResolvers = useRef<Map<string, (ok: boolean) => void>>(new Map());

  const stopQuizSpeech = (reason: string): void => {
    for (const [, resolve] of pendingSpeakResolvers.current) {
      resolve(false);
    }
    pendingSpeakResolvers.current.clear();

    if (hasWebViewBridge) {
      postToHost("quiz-stop-speech", { reason });
    }

    stopBrowserSpeech();
    setIsSpeaking(false);
  };

  // ── AI response listener ────────────────────────────────────────────────────
  useEffect(() => {
    const onMessage = (event: MessageEvent) => {
      const data = event.data as { type?: string; payload?: QuizGenerateResult | QuizSpeakResult } | undefined;
      if (!data) return;

      if (data.type === "quiz-speak-result") {
        const payload = data.payload as QuizSpeakResult | undefined;
        const requestId = payload?.requestId ?? "";
        if (!requestId) return;
        const resolve = pendingSpeakResolvers.current.get(requestId);
        if (!resolve) return;
        pendingSpeakResolvers.current.delete(requestId);
        resolve(Boolean(payload?.ok));
        return;
      }

      if (data.type !== "quiz-generate-questions-result") return;

      const payload = data.payload as QuizGenerateResult | undefined;
      if (!payload) {
        setIsGenerating(false);
        setGenerationError("AI returned an empty response.");
        return;
      }
      if (!payload.ok) {
        setIsGenerating(false);
        setGenerationError(sanitizeHostError(payload.error));
        return;
      }
      const raw = Array.isArray(payload.questions) ? payload.questions : [];
      const parsed = raw
        .map((q, i) => parseIncomingQuestion(q, i))
        .filter((q): q is Question => q !== null);
      if (parsed.length === 0) {
        setIsGenerating(false);
        setGenerationError("AI returned questions but none passed client validation.");
        return;
      }
      if (hasDuplicateHeavyQuestions(parsed)) {
        setIsGenerating(false);
        setGenerationError("AI returned duplicate-heavy questions. Try generating again.");
        return;
      }
      // Store questions — do NOT auto-start
      setActiveQuestions(parsed);
      setGeneratedCount(parsed.length);
      setGenerationSource(payload.source ?? "Atlas AI");
      setGenerationError("");
      setIsGenerating(false);
      // Reset game to ready with new bank, wait for user to press Start Game
      setGameStatus("ready");
      setQuestionIndex(0);
      setAnswerRevealed(false);
      setSelectedAnswerIndex(null);
      setScoredThisQuestion(new Set());
      setScoreStatus("");
      setQuestionAnimTick((tick) => tick + 1);
      setAnswerOutcome("none");
      setSelectionPulseIndex(null);
      setTimeRemaining(Math.max(10, Number(parsed[0]?.timeLimit) || 30));
      setTimeUp(false);
      postToHost("quiz-log", { message: `AI generated ${parsed.length} questions. Ready to start.` });
    };
    const wv = (window as any)?.chrome?.webview;
    if (wv?.addEventListener) {
      wv.addEventListener("message", onMessage);
      return () => wv.removeEventListener("message", onMessage);
    }
    window.addEventListener("message", onMessage);
    return () => window.removeEventListener("message", onMessage);
  }, []);

  useEffect(() => {
    const result = savePlayers(players);
    setPlayerStorageWarning(result.warning);
  }, [players]);

  useEffect(() => {
    const activeQuestion = activeQuestions[questionIndex] ?? null;
    if (gameStatus !== "running" || !activeQuestion || answerRevealed || timeUp) return;

    const timer = window.setInterval(() => {
      setTimeRemaining((prev) => {
        if (prev <= 1) {
          window.clearInterval(timer);
          setTimeUp(true);
          setScoreStatus("Time up.");
          return 0;
        }
        return prev - 1;
      });
    }, 1000);

    return () => window.clearInterval(timer);
  }, [gameStatus, activeQuestions, questionIndex, answerRevealed, timeUp]);

  useEffect(() => {
    return () => {
      stopQuizSpeech("component-unmount");
      for (const [, resolve] of pendingSpeakResolvers.current) {
        resolve(false);
      }
      pendingSpeakResolvers.current.clear();
    };
  }, []);

  // ── Derived ────────────────────────────────────────────────────────────────
  const currentQuestion = activeQuestions[questionIndex] ?? null;
  const totalQuestions = activeQuestions.length;
  const currentRound = currentQuestion?.round ?? 1;
  const currentQuestionPoints = currentQuestion && Number.isFinite(currentQuestion.points) && currentQuestion.points > 0
    ? currentQuestion.points
    : 100;
  const currentPlayerIndex = players.findIndex((player) => player.id === currentPlayerId);
  const currentPlayer = currentPlayerIndex >= 0 ? players[currentPlayerIndex] : players[0] ?? null;
  const leaderPlayerId = players.reduce<string | null>((leaderId, player) => {
    if (!leaderId) return player.id;
    return (scores[player.id] ?? 0) > (scores[leaderId] ?? 0) ? player.id : leaderId;
  }, null);

  // ── Actions ───────────────────────────────────────────────────────────────
  const generateQuiz = () => {
    if (isGenerating) return;
    if (!hasWebViewBridge) {
      setGenerationError("Atlas AI bridge is only available inside Atlas.");
      return;
    }
    setIsGenerating(true);
    setGenerationError("");
    stopQuizSpeech("generate-quiz");
    const count = clampQuestionCount(questionCount);
    const rounds = Math.max(1, Math.min(totalRounds, count));
    postToHost("quiz-generate-questions", {
      topic: prompt.trim() || "General Knowledge",
      difficulty,
      count,
      rounds,
      multipleChoice: true,
      timeLimit: 30,
      points: 100,
    });
  };

  useEffect(() => {
    if (players.length === 0) {
      setCurrentPlayerId("");
      return;
    }

    if (!players.some((player) => player.id === currentPlayerId)) {
      setCurrentPlayerId(players[0].id);
    }
  }, [players, currentPlayerId]);

  const advanceTurn = () => {
    if (players.length <= 1) {
      if (players[0]) setCurrentPlayerId(players[0].id);
      return;
    }

    const activeIndex = players.findIndex((player) => player.id === currentPlayerId);
    const baseIndex = activeIndex >= 0 ? activeIndex : 0;
    const nextIndex = (baseIndex + 1) % players.length;
    setCurrentPlayerId(players[nextIndex].id);
  };

  const handleStartGame = () => {
    if (isPlaying) {
      setScoreStatus("Game is already running.");
      return;
    }
    if (activeQuestions.length === 0) {
      setGenerationError("Generate a quiz first using the AI Quiz Generator →");
      return;
    }
    if (isFinished) {
      setGenerationError("Use Reset & Play Again to start over.");
      return;
    }
    stopQuizSpeech("start-game");
    setGameStatus("running");
    setQuestionIndex(0);
    setAnswerRevealed(false);
    setSelectedAnswerIndex(null);
    setScoredThisQuestion(new Set());
    setScoreStatus("");
    setAnswerOutcome("none");
    setSelectionPulseIndex(null);
    setLastScoredPlayerId(null);
    setCurrentPlayerId(players[0]?.id ?? "");
    setTimeRemaining(Math.max(10, Number(activeQuestions[0]?.timeLimit) || 30));
    setTimeUp(false);
    postToHost("quiz-log", { message: "Game started." });
  };

  const handleReveal = () => {
    if (!currentQuestion || answerRevealed) return;
    setAnswerRevealed(true);
    if (selectedAnswerIndex !== null) {
      const outcome = selectedAnswerIndex === currentQuestion.correctAnswer ? "correct" : "wrong";
      setAnswerOutcome(outcome);
      playSoundHook(outcome === "correct" ? "correct" : "wrong");
    }
    playSoundHook("reveal");
    setScoreStatus("");
    setGameStatus("running");
    postToHost("quiz-log", { message: `Answer revealed: option ${currentQuestion.correctAnswer}` });
  };

  const handleScore = (playerId: string) => {
    const player = players.find((p) => p.id === playerId);
    console.log(`[AIQuiz] score.click playerId=${playerId} name=${player?.name ?? "unknown"}`);
    if (!player) {
      setScoreStatus("Unable to award: player not found.");
      return;
    }
    if (!currentQuestion) {
      setScoreStatus("No active question.");
      return;
    }
    if (!answerRevealed) {
      setScoreStatus("Reveal the answer first.");
      return;
    }
    const guardKey = buildScoreGuardKey(questionIndex, player.id);
    if (scoredThisQuestion.has(guardKey)) {
      setScoreStatus("Score blocked: already awarded");
      console.log(`[AIQuiz] score.blocked reason=already-awarded playerId=${player.id} questionIndex=${questionIndex}`);
      return;
    }
    const awardedPoints = Number.isFinite(currentQuestion.points) && currentQuestion.points > 0
      ? currentQuestion.points
      : 100;
    let nextTotal = 0;
    setScores((s) => {
      nextTotal = (s[player.id] ?? 0) + awardedPoints;
      return { ...s, [player.id]: nextTotal };
    });
    setScoredThisQuestion((prev) => new Set([...prev, guardKey]));
    setScoreStatus(`Score click: ${player.name} +${awardedPoints}`);
    window.setTimeout(() => setScoreStatus(`${player.name} awarded ${awardedPoints}pts.`), 120);
    setLastScoredPlayerId(player.id);
    console.log(`[AIQuiz] score.award playerId=${player.id} points=${awardedPoints} total=${nextTotal}`);
    playSoundHook("score");
    postToHost("quiz-log", { message: `${player.name} scored ${awardedPoints} points.` });
  };

  const handleNext = () => {
    stopQuizSpeech("next-question");
    const nextIndex = questionIndex + 1;
    if (nextIndex >= totalQuestions) {
      setGameStatus("finished");
      playSoundHook("finish");
      postToHost("quiz-log", { message: "Quiz finished." });
      return;
    }
    advanceTurn();
    playSoundHook("next");
    setQuestionIndex(nextIndex);
    setAnswerRevealed(false);
    setSelectedAnswerIndex(null);
    setScoredThisQuestion(new Set());
    setScoreStatus("");
    setAnswerOutcome("none");
    setSelectionPulseIndex(null);
    setTimeRemaining(Math.max(10, Number(activeQuestions[nextIndex]?.timeLimit) || 30));
    setTimeUp(false);
    setQuestionAnimTick((tick) => tick + 1);
    postToHost("quiz-log", { message: `Advanced to question ${nextIndex + 1}.` });
  };

  const handleEndQuiz = () => {
    stopQuizSpeech("end-quiz");
    setGameStatus("finished");
    setTimeRemaining(0);
    setTimeUp(false);
    playSoundHook("finish");
    postToHost("quiz-log", { message: "Quiz ended manually." });
  };

  const handleReset = () => {
    stopQuizSpeech("reset-quiz");
    setGameStatus("ready");
    setQuestionIndex(0);
    setAnswerRevealed(false);
    setSelectedAnswerIndex(null);
    setScoredThisQuestion(new Set());
    setScoreStatus("");
    setAnswerOutcome("none");
    setSelectionPulseIndex(null);
    setLastScoredPlayerId(null);
    setCurrentPlayerId(players[0]?.id ?? "");
    setTimeRemaining(0);
    setTimeUp(false);
    const resetScores: Record<string, number> = {};
    players.forEach((p) => { resetScores[p.id] = 0; });
    setScores(resetScores);
    postToHost("quiz-log", { message: "Quiz reset." });
  };

  const handlePause = () => {
    if (!isPlaying) {
      setGenerationError("Pause is only available during a live game.");
      return;
    }
    setGameStatus((s) => (s === "running" ? "paused" : "running"));
  };

  const handleHostMode = () => {
    setGenerationError("Host mode is not wired yet.");
  };

  const handleSettings = () => {
    setGenerationError("Settings not wired yet.");
  };

  const handleAiVoice = () => {
    const run = async () => {
      if (isSpeaking) {
        stopQuizSpeech("voice-button-stop");
        setVoiceStatus("Voice stopped.");
        return;
      }

      if (voiceMuted) {
        setVoiceStatus("AI Voice is muted.");
        return;
      }
      if (!currentQuestion) {
        setVoiceStatus("No active question to speak.");
        return;
      }

      const correctText = currentQuestion.options[currentQuestion.correctAnswer] ?? "";
      const toSpeak = answerRevealed
        ? `Correct answer: ${correctText}. ${currentQuestion.explanation || ""}`.trim()
        : `Question: ${currentQuestion.question}. Options: ${currentQuestion.options.map((opt, i) => `${String.fromCharCode(65 + i)}: ${opt}`).join(". ")}.`;

      if (!toSpeak) {
        setVoiceStatus("AI Voice is not available on this device.");
        return;
      }

      setVoiceStatus("");
      setIsSpeaking(true);

      let spoke = false;
      if (hasWebViewBridge) {
        const requestId = `quiz-speak-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
        spoke = await new Promise<boolean>((resolve) => {
          const timeout = window.setTimeout(() => {
            pendingSpeakResolvers.current.delete(requestId);
            resolve(false);
          }, 25000);

          pendingSpeakResolvers.current.set(requestId, (ok) => {
            window.clearTimeout(timeout);
            resolve(ok);
          });

          postToHost("quiz-speak", {
            requestId,
            text: toSpeak,
            reason: answerRevealed ? "quiz-answer" : "quiz-question",
          });
        });
      }

      if (!spoke) {
        stopBrowserSpeech();
        spoke = await speakViaBrowser(toSpeak);
      }

      setIsSpeaking(false);
      setVoiceStatus(spoke ? (answerRevealed ? "Spoke answer and explanation." : "Spoke question.") : "AI Voice is not available on this device.");
    };

    void run();
  };

  const addPlayer = () => {
    const name = newPlayerName.trim() || `Player ${players.length + 1}`;
    const normalized = name.toLowerCase();
    if (players.some((p) => p.name.toLowerCase() === normalized)) return;
    const player = { id: createPlayerId(name), name };
    setPlayers((p) => [...p, player]);
    setScores((s) => ({ ...s, [player.id]: 0 }));
    setNewPlayerName("");
    setShowAddPlayer(false);
    setPlayerStatus("");
  };

  const removePlayer = (player: Player) => {
    if (players.length <= 1) {
      setPlayerStatus("At least one player is required.");
      return;
    }

    const removedIndex = players.findIndex((p) => p.id === player.id);
    const remaining = players.filter((p) => p.id !== player.id);
    setPlayers(remaining);
    setScores((prev) => {
      const next = { ...prev };
      delete next[player.id];
      return next;
    });
    setScoredThisQuestion((prev) => {
      const next = new Set(prev);
      next.delete(player.id);
      return next;
    });

    if (currentPlayerId === player.id) {
      if (remaining.length === 0) {
        setCurrentPlayerId("");
      } else {
        const safeIndex = Math.min(removedIndex, remaining.length - 1);
        setCurrentPlayerId(remaining[safeIndex].id);
      }
    }

    if (lastScoredPlayerId === player.id) {
      setLastScoredPlayerId(null);
    }

    setPlayerStatus("");
  };

  const selectAnswer = (index: number) => {
    if (!isPlaying || answerRevealed || timeUp) return;
    setSelectedAnswerIndex(index);
    setSelectionPulseIndex(index);
  };

  // ── Display helpers ────────────────────────────────────────────────────────
  const statusLabel =
    gameStatus === "ready"    ? "Ready"    :
    gameStatus === "running"  ? "Live"     :
    gameStatus === "paused"   ? "Paused"   : "Finished";

  const statusColor =
    gameStatus === "running"  ? "#22d3ee" :
    gameStatus === "paused"   ? "#fbbf24" :
    gameStatus === "finished" ? "#f87171" : "#34d399";

  const currentModeName = MODES.find((m) => m.id === selectedMode)?.label ?? "Classic Quiz";
  const isPlaying = gameStatus === "running" || gameStatus === "paused";
  const isFinished = gameStatus === "finished";

  // ── Render ────────────────────────────────────────────────────────────────
  return (
    <div
      style={{
        display: "flex",
        height: "100vh",
        overflow: "hidden",
        background: "#0b0f1a",
        color: "#e2e8f0",
        fontFamily: "'Inter', system-ui, sans-serif",
      }}
    >
      <style>{`
        @keyframes quizQuestionIn { from { opacity: 0; transform: translateY(8px); } to { opacity: 1; transform: translateY(0); } }
        @keyframes quizSelectedPulse { 0% { transform: scale(1); } 50% { transform: scale(1.02); } 100% { transform: scale(1); } }
        @keyframes quizCorrectGlow { 0% { box-shadow: 0 0 0 rgba(34,197,94,0); } 50% { box-shadow: 0 0 0 4px rgba(34,197,94,0.18); } 100% { box-shadow: 0 0 0 rgba(34,197,94,0); } }
        @keyframes quizWrongPulse { 0% { transform: translateX(0); } 25% { transform: translateX(-3px); } 50% { transform: translateX(3px); } 100% { transform: translateX(0); } }
        @keyframes quizScorePop { 0% { transform: scale(1); } 50% { transform: scale(1.06); } 100% { transform: scale(1); } }
        @keyframes quizTurnGlow { 0% { box-shadow: 0 0 0 rgba(56,189,248,0.0); } 50% { box-shadow: 0 0 0 4px rgba(56,189,248,0.16); } 100% { box-shadow: 0 0 0 rgba(56,189,248,0.0); } }
        @keyframes quizWinnerGlow { 0% { background: rgba(251,191,36,0.12); } 50% { background: rgba(251,191,36,0.2); } 100% { background: rgba(251,191,36,0.12); } }
      `}</style>
      {/* ── Left Sidebar ── */}
      <aside
        style={{
          width: 224,
          flexShrink: 0,
          background: "#0d1117",
          borderRight: "1px solid rgba(255,255,255,0.06)",
          display: "flex",
          flexDirection: "column",
        }}
      >
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: 10,
            padding: "18px 20px",
            borderBottom: "1px solid rgba(255,255,255,0.06)",
          }}
        >
          <div
            style={{
              width: 32, height: 32, borderRadius: 8,
              background: "linear-gradient(135deg,#6366f1,#8b5cf6)",
              display: "flex", alignItems: "center", justifyContent: "center",
              fontSize: 14, color: "#fff",
            }}
          >
            ✦
          </div>
          <span style={{ fontWeight: 600, fontSize: 14, color: "#e2e8f0" }}>Quiz Modes</span>
        </div>
        <nav style={{ flex: 1, overflowY: "auto", padding: "10px 0" }}>
          {MODES.map((mode) => {
            const active = selectedMode === mode.id;
            return (
              <button
                type="button"
                key={mode.id}
                onClick={() => setSelectedMode(mode.id)}
                style={{
                  display: "flex", alignItems: "center", gap: 12,
                  width: active ? "calc(100% - 16px)" : "100%",
                  margin: active ? "2px 8px" : "2px 0",
                  padding: "10px 16px", textAlign: "left",
                  background: active ? "linear-gradient(90deg,rgba(99,102,241,0.35),rgba(139,92,246,0.15))" : "transparent",
                  borderRadius: active ? 10 : 0, border: "none",
                  color: active ? "#a5b4fc" : "rgba(226,232,240,0.5)",
                  fontWeight: active ? 600 : 400, fontSize: 13, cursor: "pointer",
                  transition: "all 0.15s",
                }}
              >
                <span style={{ fontSize: 13, opacity: active ? 1 : 0.55, lineHeight: 1 }}>{mode.icon}</span>
                {mode.label}
              </button>
            );
          })}
        </nav>
      </aside>

      {/* ── Main ── */}
      <main style={{ flex: 1, display: "flex", flexDirection: "column", overflow: "hidden" }}>

        {/* ── Header ── */}
        <div style={{ padding: "24px 28px 0" }}>
          <div style={{ display: "flex", alignItems: "center", gap: 12, marginBottom: 4 }}>
            <h1 style={{ fontSize: 24, fontWeight: 700, color: "#67e8f9", margin: 0 }}>AI Quiz Night</h1>
            <span style={{ fontSize: 12, fontWeight: 500, padding: "3px 12px", borderRadius: 999, background: "rgba(139,92,246,0.22)", color: "#c4b5fd", border: "1px solid rgba(139,92,246,0.4)" }}>
              {currentModeName}
            </span>
          </div>
          <p style={{ fontSize: 13, color: "rgba(226,232,240,0.42)", margin: "0 0 20px" }}>
            Host cinematic quizzes, generate rounds instantly, and let AI run the game.
          </p>

          {/* Action buttons */}
          <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
            <button
              type="button"
              onClick={handleStartGame}
              disabled={isPlaying}
              style={{
                display: "flex", alignItems: "center", gap: 6,
                padding: "8px 16px", borderRadius: 8, border: "none",
                background: "linear-gradient(135deg,#06b6d4,#0284c7)",
                color: "#fff", fontWeight: 600, fontSize: 13,
                cursor: isPlaying ? "not-allowed" : "pointer",
                opacity: isPlaying ? 0.5 : 1,
              }}
            >▶ Start Game</button>
            <button
              type="button"
              onClick={generateQuiz}
              disabled={isGenerating}
              style={{
                display: "flex", alignItems: "center", gap: 6,
                padding: "8px 16px", borderRadius: 8,
                background: "rgba(255,255,255,0.07)", border: "1px solid rgba(255,255,255,0.12)",
                color: "#e2e8f0", fontSize: 13, cursor: "pointer", opacity: isGenerating ? 0.5 : 1,
              }}
            >+ {isGenerating ? "Generating…" : "Generate Quiz"}</button>
            <button
              type="button"
              onClick={() => setShowAddPlayer((v) => !v)}
              style={{
                display: "flex", alignItems: "center", gap: 6,
                padding: "8px 16px", borderRadius: 8,
                background: "rgba(255,255,255,0.07)", border: "1px solid rgba(255,255,255,0.12)",
                color: "#e2e8f0", fontSize: 13, cursor: "pointer",
              }}
            >👤 Add Player</button>
            <button
              type="button"
              onClick={handleHostMode}
              style={{
                display: "flex", alignItems: "center", gap: 6,
                padding: "8px 16px", borderRadius: 8,
                background: "rgba(255,255,255,0.07)", border: "1px solid rgba(255,255,255,0.12)",
                color: "rgba(226,232,240,0.4)", fontSize: 13, cursor: "pointer",
              }}
              title="Multiplayer join is not wired yet"
            >🔗 Host Mode</button>
            <button
              type="button"
              onClick={handleSettings}
              style={{
                display: "flex", alignItems: "center", gap: 6,
                padding: "8px 16px", borderRadius: 8,
                background: "rgba(255,255,255,0.07)", border: "1px solid rgba(255,255,255,0.12)",
                color: "#e2e8f0", fontSize: 13, cursor: "pointer",
              }}
            >⚙ Settings</button>
          </div>

          {/* Add player */}
          {showAddPlayer && (
            <div style={{ display: "flex", gap: 8, marginTop: 10, alignItems: "center" }}>
              <input
                autoFocus
                placeholder="Player name"
                value={newPlayerName}
                onChange={(e) => setNewPlayerName(e.target.value)}
                onKeyDown={(e) => e.key === "Enter" && addPlayer()}
                style={{ width: 180, padding: "6px 12px", borderRadius: 8, background: "rgba(255,255,255,0.07)", border: "1px solid rgba(255,255,255,0.15)", color: "#e2e8f0", fontSize: 13, outline: "none" }}
              />
              <button type="button" onClick={addPlayer} style={{ padding: "6px 14px", borderRadius: 8, background: "#6366f1", border: "none", color: "#fff", fontSize: 13, cursor: "pointer" }}>Add</button>
              <button type="button" onClick={() => setShowAddPlayer(false)} style={{ padding: "6px 14px", borderRadius: 8, background: "rgba(255,255,255,0.07)", border: "1px solid rgba(255,255,255,0.1)", color: "#94a3b8", fontSize: 13, cursor: "pointer" }}>Cancel</button>
            </div>
          )}

          {/* Player chips */}
          {players.length > 0 && (
            <div style={{ display: "flex", gap: 6, flexWrap: "wrap", marginTop: 10 }}>
              {players.map((p) => (
                <span
                  key={p.id}
                  style={{
                    fontSize: 12,
                    padding: "3px 8px 3px 10px",
                    borderRadius: 999,
                    background: p.id === currentPlayerId ? "rgba(56,189,248,0.16)" : "rgba(99,102,241,0.18)",
                    color: p.id === currentPlayerId ? "#a5f3fc" : "#a5b4fc",
                    border: `1px solid ${p.id === currentPlayerId ? "rgba(56,189,248,0.45)" : "rgba(99,102,241,0.3)"}`,
                    display: "inline-flex",
                    alignItems: "center",
                    gap: 6,
                    animation: p.id === currentPlayerId
                      ? "quizTurnGlow 1.8s ease-in-out infinite"
                      : lastScoredPlayerId === p.id
                      ? "quizScorePop 380ms ease"
                      : undefined,
                  }}
                >
                  {leaderPlayerId === p.id ? "👑 " : ""}{p.name}
                  <span
                    style={{
                      borderRadius: 999,
                      padding: "1px 7px",
                      background: "rgba(15,23,42,0.45)",
                      border: "1px solid rgba(148,163,184,0.25)",
                      color: "rgba(226,232,240,0.9)",
                      fontSize: 11,
                      fontWeight: 600,
                    }}
                  >
                    {scores[p.id] ?? 0}pts
                  </span>
                  {p.id === currentPlayerId && (
                    <span style={{ fontSize: 10, fontWeight: 700, color: "#67e8f9", letterSpacing: 0.4 }}>
                      TURN
                    </span>
                  )}
                  <button
                    type="button"
                    onClick={(event) => {
                      event.preventDefault();
                      event.stopPropagation();
                      removePlayer(p);
                    }}
                    style={{
                      border: "none",
                      background: "transparent",
                      color: "rgba(165,180,252,0.85)",
                      cursor: "pointer",
                      fontSize: 13,
                      lineHeight: 1,
                      padding: "0 4px",
                      minWidth: 16,
                      minHeight: 16,
                      display: "inline-flex",
                      alignItems: "center",
                      justifyContent: "center",
                    }}
                    title={`Remove ${p.name}`}
                    aria-label={`Remove ${p.name}`}
                  >
                    ×
                  </button>
                </span>
              ))}
            </div>
          )}

          {currentPlayer && (
            <div style={{ marginTop: 6, fontSize: 11, color: "rgba(103,232,249,0.9)" }}>
              Current turn: {currentPlayer.name}
            </div>
          )}

          {playerStatus && (
            <div style={{ marginTop: 6, fontSize: 11, color: "rgba(251,191,36,0.9)" }}>
              {playerStatus}
            </div>
          )}

          <div style={{ borderBottom: "1px solid rgba(255,255,255,0.07)", marginTop: 18 }} />
        </div>

        {/* ── Two-panel body ── */}
        <div style={{ flex: 1, overflow: "auto", padding: "20px 28px" }}>
          <div style={{ display: "grid", gridTemplateColumns: "1fr 370px", gap: 16, height: "100%" }}>

            {/* ── Live Game Control ── */}
            <div style={CARD}>
              {/* Card header */}
              <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
                <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
                  <div style={{ width: 34, height: 34, borderRadius: 8, background: "rgba(6,182,212,0.14)", border: "1px solid rgba(6,182,212,0.28)", display: "flex", alignItems: "center", justifyContent: "center", color: "#22d3ee", fontSize: 13 }}>▶</div>
                  <span style={{ fontWeight: 600, fontSize: 15 }}>Live Game Control</span>
                </div>
                <span style={{ fontSize: 12, fontWeight: 600, padding: "4px 12px", borderRadius: 999, border: `1px solid ${statusColor}55`, color: statusColor, background: `${statusColor}15` }}>
                  {statusLabel}
                </span>
              </div>

              {/* Big CTA — only shown when ready/finished */}
              {!isPlaying && (
                <button
                  type="button"
                  onClick={handleStartGame}
                  disabled={activeQuestions.length === 0}
                  style={{
                    width: "100%", padding: "18px", borderRadius: 14, border: "none",
                    background: activeQuestions.length === 0
                      ? "rgba(255,255,255,0.06)"
                      : "linear-gradient(135deg,#a855f7 0%,#ec4899 100%)",
                    color: activeQuestions.length === 0 ? "rgba(226,232,240,0.3)" : "#fff",
                    fontSize: 17, fontWeight: 700,
                    cursor: activeQuestions.length === 0 ? "not-allowed" : "pointer",
                    boxShadow: activeQuestions.length === 0 ? "none" : "0 6px 40px rgba(168,85,247,0.4)",
                    display: "flex", alignItems: "center", justifyContent: "center", gap: 10,
                    transition: "all 0.15s",
                  }}
                >
                  <span style={{ fontSize: 18 }}>✦</span>
                  {activeQuestions.length === 0 ? "Generate a quiz first →" : isFinished ? "Play Again" : "Start AI Hosted Quiz"}
                </button>
              )}

              {/* Status + Round tiles */}
              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
                <div style={{ padding: "14px 16px", borderRadius: 12, background: "linear-gradient(135deg,rgba(6,182,212,0.12),rgba(20,184,166,0.07))", border: "1px solid rgba(6,182,212,0.22)" }}>
                  <p style={{ fontSize: 11, color: "rgba(226,232,240,0.42)", margin: "0 0 4px" }}>Status</p>
                  <p style={{ fontSize: 14, fontWeight: 600, color: "#67e8f9", margin: 0 }}>{statusLabel}</p>
                </div>
                <div style={{ padding: "14px 16px", borderRadius: 12, background: "linear-gradient(135deg,rgba(139,92,246,0.12),rgba(99,102,241,0.07))", border: "1px solid rgba(139,92,246,0.22)" }}>
                  <p style={{ fontSize: 11, color: "rgba(226,232,240,0.42)", margin: "0 0 4px" }}>Round</p>
                  <p style={{ fontSize: 14, fontWeight: 600, color: "#c4b5fd", margin: 0 }}>{currentRound} of {totalRounds}</p>
                </div>
              </div>

              <div style={{ padding: "10px 14px", borderRadius: 12, background: "rgba(251,191,36,0.08)", border: "1px solid rgba(251,191,36,0.25)", display: "flex", alignItems: "center", justifyContent: "space-between" }}>
                <span style={{ fontSize: 11, color: "rgba(226,232,240,0.5)" }}>Question Timer</span>
                <span style={{ fontSize: 13, fontWeight: 700, color: timeUp ? "#fca5a5" : "#fde68a" }}>
                  {isPlaying && currentQuestion ? `${timeRemaining}s` : "--"}
                </span>
              </div>

              {timeUp && !answerRevealed && (
                <div style={{ padding: "8px 12px", borderRadius: 10, fontSize: 12, background: "rgba(239,68,68,0.1)", border: "1px solid rgba(239,68,68,0.25)", color: "#fca5a5" }}>
                  Time up.
                </div>
              )}

              {/* ── Active question area (only when game is running/paused/finished) ── */}
              {(isPlaying || isFinished) && currentQuestion && (
                <div
                  key={`${currentQuestion.id}-${questionAnimTick}`}
                  style={{
                    background: "rgba(255,255,255,0.03)",
                    border: "1px solid rgba(255,255,255,0.09)",
                    borderRadius: 14,
                    padding: 16,
                    display: "flex",
                    flexDirection: "column",
                    gap: 12,
                    animation: "quizQuestionIn 220ms ease",
                  }}
                >
                  {/* Question meta */}
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                    <span style={{ fontSize: 11, color: "rgba(226,232,240,0.38)" }}>
                      Q {questionIndex + 1} of {totalQuestions} · {currentQuestion.category}
                    </span>
                    <span
                      style={{
                        fontSize: 11, padding: "2px 8px", borderRadius: 999,
                        background: "rgba(139,92,246,0.18)", color: "#c4b5fd",
                        border: "1px solid rgba(139,92,246,0.3)",
                      }}
                    >
                      {currentQuestion.difficulty} · {currentQuestion.points}pts
                    </span>
                  </div>

                  {/* Question text */}
                  <p style={{ fontSize: 15, fontWeight: 600, color: "#e2e8f0", margin: 0, lineHeight: 1.5 }}>
                    {currentQuestion.question}
                  </p>

                  {/* Options grid */}
                  <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 8 }}>
                    {currentQuestion.options.map((opt, i) => {
                      const isCorrect = i === currentQuestion.correctAnswer;
                      const isSelected = selectedAnswerIndex === i;
                      const showCorrect = answerRevealed && isCorrect;
                      const showWrongSelected = answerRevealed && isSelected && !isCorrect;
                      const revealed = answerRevealed;
                      return (
                        <button
                          type="button"
                          key={`${currentQuestion.id}-${i}`}
                          onClick={() => selectAnswer(i)}
                          disabled={revealed || !isPlaying || timeUp}
                          style={{
                            padding: "10px 12px",
                            borderRadius: 10,
                            fontSize: 13,
                            textAlign: "left",
                            background: showCorrect
                              ? "rgba(34,197,94,0.18)"
                              : showWrongSelected
                              ? "rgba(239,68,68,0.18)"
                              : isSelected
                              ? "rgba(99,102,241,0.22)"
                              : "rgba(255,255,255,0.04)",
                            border: `1px solid ${showCorrect ? "rgba(34,197,94,0.5)" : showWrongSelected ? "rgba(239,68,68,0.5)" : isSelected ? "rgba(99,102,241,0.5)" : "rgba(255,255,255,0.08)"}`,
                            color: showCorrect ? "#86efac" : showWrongSelected ? "#fca5a5" : "rgba(226,232,240,0.75)",
                            fontWeight: showCorrect || showWrongSelected || isSelected ? 600 : 400,
                            transition: "all 0.2s",
                            cursor: revealed || !isPlaying || timeUp ? "default" : "pointer",
                            opacity: revealed || !isPlaying || timeUp ? 0.95 : 1,
                            animation: showCorrect
                              ? "quizCorrectGlow 420ms ease"
                              : showWrongSelected
                              ? "quizWrongPulse 280ms ease"
                              : selectionPulseIndex === i
                              ? "quizSelectedPulse 180ms ease"
                              : undefined,
                          }}
                        >
                          <span style={{ fontWeight: 600, marginRight: 6, opacity: 0.6 }}>
                            {String.fromCharCode(65 + i)}.
                          </span>
                          {opt}
                        </button>
                      );
                    })}
                  </div>

                  {/* Explanation (after reveal) */}
                  {answerRevealed && currentQuestion.explanation && (
                    <div
                      style={{
                        padding: "10px 12px", borderRadius: 10, fontSize: 12,
                        background: "rgba(6,182,212,0.08)", border: "1px solid rgba(6,182,212,0.2)",
                        color: "#a5f3fc", lineHeight: 1.5,
                      }}
                    >
                      <span style={{ fontWeight: 600, marginRight: 4 }}>📖</span>
                      {currentQuestion.explanation}
                    </div>
                  )}

                  {/* Score buttons (after reveal) */}
                  {answerRevealed && players.length > 0 && (
                    <div>
                      <p style={{ fontSize: 11, color: "rgba(226,232,240,0.4)", margin: "0 0 6px" }}>Award {currentQuestionPoints}pts to:</p>
                      <div style={{ display: "flex", gap: 6, flexWrap: "wrap" }}>
                        {players.map((p) => (
                          <button
                            type="button"
                            key={p.id}
                            onClick={() => handleScore(p.id)}
                            style={{
                              padding: "5px 12px", borderRadius: 8, border: "none",
                              background: scoredThisQuestion.has(buildScoreGuardKey(questionIndex, p.id))
                                ? "rgba(34,197,94,0.2)"
                                : p.id === currentPlayerId
                                ? "linear-gradient(135deg,#0ea5e9,#0284c7)"
                                : "#6366f1",
                              color: scoredThisQuestion.has(buildScoreGuardKey(questionIndex, p.id)) ? "#86efac" : "#fff",
                              fontSize: 12, cursor: "pointer",
                              opacity: scoredThisQuestion.has(buildScoreGuardKey(questionIndex, p.id)) ? 0.7 : 1,
                              boxShadow: p.id === currentPlayerId && !scoredThisQuestion.has(buildScoreGuardKey(questionIndex, p.id))
                                ? "0 0 0 1px rgba(103,232,249,0.35)"
                                : "none",
                            }}
                          >
                            {scoredThisQuestion.has(buildScoreGuardKey(questionIndex, p.id))
                              ? `✓ ${p.name}`
                              : p.id === currentPlayerId
                              ? `+pts ${p.name} (TURN)`
                              : `+pts ${p.name}`}
                          </button>
                        ))}
                      </div>
                      {scoreStatus && (
                        <p style={{ fontSize: 11, color: "rgba(251,191,36,0.9)", margin: "6px 0 0" }}>
                          {scoreStatus}
                        </p>
                      )}
                    </div>
                  )}

                  {/* Game control buttons */}
                  <div style={{ display: "flex", gap: 8, flexWrap: "wrap", paddingTop: 4 }}>
                    {!answerRevealed && (
                      <button
                        type="button"
                        onClick={handleReveal}
                        style={{
                          padding: "8px 14px", borderRadius: 8, border: "none",
                          background: "linear-gradient(135deg,#22c55e,#16a34a)",
                          color: "#fff", fontSize: 12, fontWeight: 600, cursor: "pointer",
                        }}
                      >🔍 Reveal Answer</button>
                    )}
                    {answerRevealed && (
                      <button
                        type="button"
                        onClick={handleNext}
                        style={{
                          padding: "8px 14px", borderRadius: 8, border: "none",
                          background: "linear-gradient(135deg,#06b6d4,#0284c7)",
                          color: "#fff", fontSize: 12, fontWeight: 600, cursor: "pointer",
                        }}
                      >{questionIndex + 1 >= totalQuestions ? "🏁 Finish Quiz" : "⏭ Next Question"}</button>
                    )}
                    <button
                      type="button"
                      onClick={handleEndQuiz}
                      style={{
                        padding: "8px 14px", borderRadius: 8,
                        background: "rgba(255,255,255,0.05)", border: "1px solid rgba(255,255,255,0.1)",
                        color: "#94a3b8", fontSize: 12, cursor: "pointer",
                      }}
                    >■ End Quiz</button>
                  </div>
                </div>
              )}

              {/* Final scoreboard */}
              {isFinished && (
                <div
                  style={{
                    background: "rgba(255,255,255,0.03)",
                    border: "1px solid rgba(255,255,255,0.08)",
                    borderRadius: 14,
                    padding: 16,
                  }}
                >
                  <p style={{ fontSize: 13, fontWeight: 600, color: "#c4b5fd", margin: "0 0 10px" }}>🏆 Final Scores</p>
                  {[...players]
                    .sort((a, b) => (scores[b.id] ?? 0) - (scores[a.id] ?? 0))
                    .map((p, i) => (
                      <div key={p.id} style={{ display: "flex", justifyContent: "space-between", padding: "6px 0", borderBottom: i < players.length - 1 ? "1px solid rgba(255,255,255,0.05)" : "none", animation: i === 0 ? "quizWinnerGlow 1.4s ease-in-out infinite" : undefined, borderRadius: 8 }}>
                        <span style={{ fontSize: 13, color: i === 0 ? "#fbbf24" : "rgba(226,232,240,0.7)" }}>
                          {i === 0 ? "👑 " : `${i + 1}. `}{p.name}
                        </span>
                        <span style={{ fontSize: 13, fontWeight: 600, color: i === 0 ? "#fbbf24" : "#67e8f9" }}>
                          {scores[p.id] ?? 0} pts
                        </span>
                      </div>
                    ))}
                  <button
                    type="button"
                    onClick={handleReset}
                    style={{
                      marginTop: 12, width: "100%", padding: "8px", borderRadius: 8, border: "none",
                      background: "rgba(99,102,241,0.2)", color: "#a5b4fc", fontSize: 12, cursor: "pointer",
                    }}
                  >↺ Reset & Play Again</button>
                </div>
              )}

              {/* Pause + AI Voice */}
              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
                <button
                  type="button"
                  onClick={handlePause}
                  disabled={!isPlaying}
                  style={{
                    display: "flex", alignItems: "center", justifyContent: "center", gap: 8,
                    padding: "12px", borderRadius: 12,
                    background: "rgba(255,255,255,0.05)", border: "1px solid rgba(255,255,255,0.1)",
                    color: "#e2e8f0", fontSize: 13, cursor: isPlaying ? "pointer" : "not-allowed",
                    opacity: isPlaying ? 1 : 0.35,
                  }}
                >⏸ {gameStatus === "paused" ? "Resume" : "Pause"}</button>
                <button
                  type="button"
                  onClick={handleAiVoice}
                  style={{
                    display: "flex", alignItems: "center", justifyContent: "center", gap: 8,
                    padding: "12px", borderRadius: 12,
                    background: "rgba(6,182,212,0.1)", border: "1px solid rgba(6,182,212,0.25)",
                    color: "#67e8f9", fontSize: 13, cursor: "pointer",
                  }}
                >{isSpeaking ? "🛑 Stop Voice (Speaking…)" : voiceMuted ? "🔇 AI Voice" : "🔊 AI Voice"}</button>
              </div>

              {voiceStatus && (
                <div style={{ padding: "6px 10px", borderRadius: 10, fontSize: 11, background: "rgba(255,255,255,0.03)", border: "1px solid rgba(255,255,255,0.07)", color: "rgba(226,232,240,0.65)" }}>
                  {voiceStatus}
                </div>
              )}

              {playerStorageWarning && (
                <div style={{ padding: "6px 10px", borderRadius: 10, fontSize: 11, background: "rgba(251,191,36,0.08)", border: "1px solid rgba(251,191,36,0.22)", color: "rgba(251,191,36,0.95)" }}>
                  {playerStorageWarning}
                </div>
              )}

              {/* Round stepper */}
              <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", paddingTop: 4 }}>
                <span style={{ fontSize: 12, color: "rgba(226,232,240,0.38)" }}>Total Rounds</span>
                <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
                  <button type="button" onClick={() => setTotalRounds((r) => Math.max(1, r - 1))} style={{ width: 28, height: 28, borderRadius: 8, border: "none", background: "rgba(255,255,255,0.07)", color: "#94a3b8", fontSize: 16, cursor: "pointer", display: "flex", alignItems: "center", justifyContent: "center" }}>−</button>
                  <span style={{ fontSize: 14, fontWeight: 600, minWidth: 20, textAlign: "center" }}>{totalRounds}</span>
                  <button type="button" onClick={() => setTotalRounds((r) => Math.min(20, r + 1))} style={{ width: 28, height: 28, borderRadius: 8, border: "none", background: "rgba(255,255,255,0.07)", color: "#94a3b8", fontSize: 16, cursor: "pointer", display: "flex", alignItems: "center", justifyContent: "center" }}>+</button>
                </div>
              </div>

              <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", paddingTop: 2 }}>
                <span style={{ fontSize: 12, color: "rgba(226,232,240,0.38)" }}>Question Count</span>
                <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
                  <button type="button" onClick={() => setQuestionCount((n) => clampQuestionCount(n - 1))} style={{ width: 28, height: 28, borderRadius: 8, border: "none", background: "rgba(255,255,255,0.07)", color: "#94a3b8", fontSize: 16, cursor: "pointer", display: "flex", alignItems: "center", justifyContent: "center" }}>−</button>
                  <span style={{ fontSize: 14, fontWeight: 600, minWidth: 20, textAlign: "center" }}>{questionCount}</span>
                  <button type="button" onClick={() => setQuestionCount((n) => clampQuestionCount(n + 1))} style={{ width: 28, height: 28, borderRadius: 8, border: "none", background: "rgba(255,255,255,0.07)", color: "#94a3b8", fontSize: 16, cursor: "pointer", display: "flex", alignItems: "center", justifyContent: "center" }}>+</button>
                </div>
              </div>
            </div>

            {/* ── AI Quiz Generator ── */}
            <div style={CARD}>
              <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
                <div style={{ width: 34, height: 34, borderRadius: 8, background: "rgba(139,92,246,0.18)", border: "1px solid rgba(139,92,246,0.35)", display: "flex", alignItems: "center", justifyContent: "center", color: "#c4b5fd", fontSize: 14 }}>✦</div>
                <span style={{ fontWeight: 600, fontSize: 15 }}>AI Quiz Generator</span>
              </div>

              <textarea
                rows={6}
                placeholder="Create a 5-round quiz about 90s movies, football, music, and general knowledge"
                value={prompt}
                onChange={(e) => setPrompt(e.target.value)}
                style={{
                  width: "100%", padding: "12px 14px", borderRadius: 12,
                  background: "rgba(255,255,255,0.04)", border: "1px solid rgba(255,255,255,0.1)",
                  color: "#e2e8f0", fontSize: 13, lineHeight: 1.6, resize: "none",
                  outline: "none", boxSizing: "border-box",
                }}
              />

              <div>
                <p style={{ fontSize: 12, color: "rgba(226,232,240,0.45)", margin: "0 0 8px" }}>Difficulty</p>
                <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
                  {DIFFICULTIES.map((d) => {
                    const active = difficulty === d;
                    const activeBg = d === "Easy" ? "#22c55e" : d === "Medium" ? "#3b82f6" : d === "Hard" ? "#f97316" : "#ef4444";
                    return (
                      <button
                        type="button"
                        key={d}
                        onClick={() => setDifficulty(d)}
                        style={{
                          padding: "6px 14px", borderRadius: 8,
                          border: active ? "none" : "1px solid rgba(255,255,255,0.1)",
                          background: active ? activeBg : "rgba(255,255,255,0.06)",
                          color: active ? "#fff" : "rgba(226,232,240,0.5)",
                          fontSize: 13, fontWeight: active ? 600 : 400, cursor: "pointer",
                          transition: "all 0.15s",
                        }}
                      >{d}</button>
                    );
                  })}
                </div>
              </div>

              {/* Error */}
              {generationError && (
                <div style={{ padding: "8px 12px", borderRadius: 10, fontSize: 12, background: "rgba(239,68,68,0.1)", border: "1px solid rgba(239,68,68,0.25)", color: "#fca5a5" }}>
                  {generationError}
                </div>
              )}

              {/* Loaded confirmation */}
              {generatedCount > 0 && !generationError && (
                <div style={{ padding: "8px 12px", borderRadius: 10, fontSize: 12, background: "rgba(34,197,94,0.1)", border: "1px solid rgba(34,197,94,0.25)", color: "#86efac" }}>
                  ✓ {generatedCount} questions loaded from {generationSource} — press Start Game
                </div>
              )}

              {/* Multiplayer honest state */}
              <div style={{ padding: "8px 12px", borderRadius: 10, fontSize: 11, background: "rgba(255,255,255,0.03)", border: "1px solid rgba(255,255,255,0.07)", color: "rgba(226,232,240,0.3)" }}>
                Multiplayer join is not wired yet
              </div>

              <div style={{ flex: 1 }} />

              <button
                type="button"
                onClick={generateQuiz}
                disabled={isGenerating}
                style={{
                  width: "100%", padding: "14px", borderRadius: 12, border: "none",
                  background: "linear-gradient(135deg,#22c55e 0%,#16a34a 100%)",
                  color: "#fff", fontSize: 14, fontWeight: 700,
                  cursor: isGenerating ? "not-allowed" : "pointer",
                  opacity: isGenerating ? 0.55 : 1,
                  boxShadow: "0 4px 28px rgba(34,197,94,0.28)",
                  display: "flex", alignItems: "center", justifyContent: "center", gap: 8,
                  transition: "opacity 0.15s",
                }}
              >
                <span>✦</span>
                {isGenerating ? "Generating…" : "Generate Full Quiz"}
              </button>
            </div>

          </div>
        </div>
      </main>
    </div>
  );
}
