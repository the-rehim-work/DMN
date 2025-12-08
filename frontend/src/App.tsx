import { useState, useEffect, useMemo, useCallback, useRef } from 'react';
import { X, RotateCcw, Users, Trophy, Layers, Volume2, VolumeX } from 'lucide-react';
import * as GL from './gameLogic';
import { useSound } from './hooks/useSound';

interface User {
  id: string;
  userName: string;
  email: string;
  displayName: string;
}

interface ApiGame {
  id: string;
  code: string;
  mode: string;
  status: string;
  maxPlayers: number;
  minPlayers: number;
  isTeamGame: boolean;
  roundNumber: number;
  team1Score: number;
  team2Score: number;
  currentTurn: number;
  boardLeft: number | null;
  boardRight: number | null;
  outcome?: string;
  reason?: string;
  winnerId?: string;
  winningTeam?: number;
  boneyardCount: number;
  playableBoneyardCount: number;
  deferredPoints?: number;
  participants: {
    userId: string;
    displayName: string;
    position: number;
    team: number;
    color: string;
    totalScore: number;
    roundScore: number;
    tileCount: number;
    isCurrentTurn: boolean;
    hasVotedToStart: boolean;
  }[];
  board: {
    left: number;
    right: number;
    side: string;
    isFlipped: boolean;
    playedById?: string;
    playedByPosition?: number;
    playedByColor?: string;
  }[];
  myHand: number[][];
  allHands?: {
    userId: string;
    position: number;
    hand: number[][];
  }[] | null;
  myPosition?: number;
  myTeam?: number;
  roundHistory: {
    roundNumber: number;
    winnerId: string | null;
    winningTeam: number | null;
    pointsWon: number;
    reason: string;
  }[];
  requiredFirstTile?: number[] | null;
}

const API_BASE = 'http://172.22.111.136:8000/api';

function DominoPips({ value, size = 'normal', horizontal = false }: { value: number; size?: 'small' | 'normal'; horizontal?: boolean }) {
  const pipSize = size === 'small' ? 'w-1 h-1' : 'w-1.5 h-1.5';

  const positions: Record<number, [number, number][]> = {
    0: [],
    1: [[50, 50]],
    2: [[28, 28], [72, 72]],
    3: [[28, 28], [50, 50], [72, 72]],
    4: [[28, 28], [72, 28], [28, 72], [72, 72]],
    5: [[28, 28], [72, 28], [50, 50], [28, 72], [72, 72]],
    6: horizontal
      ? [[25, 28], [50, 28], [75, 28], [25, 72], [50, 72], [75, 72]]
      : [[28, 25], [72, 25], [28, 50], [72, 50], [28, 75], [72, 75]],
  };

  return (
    <div className="relative w-full h-full">
      {positions[value]?.map(([x, y], i) => (
        <div
          key={i}
          className={`absolute ${pipSize} bg-slate-900 rounded-full`}
          style={{ left: `${x}%`, top: `${y}%`, transform: 'translate(-50%, -50%)' }}
        />
      ))}
    </div>
  );
}

function DominoTileView({
  tile,
  horizontal = false,
  selected = false,
  playable = false,
  onClick,
  size = 'normal',
  glowColor,
  disabled = false,
}: {
  tile: GL.Tile;
  horizontal?: boolean;
  selected?: boolean;
  playable?: boolean;
  onClick?: () => void;
  size?: 'tiny' | 'small' | 'normal';
  glowColor?: string;
  disabled?: boolean;
}) {
  const sizeClasses = {
    tiny: horizontal ? 'w-10 h-5' : 'w-5 h-10',
    small: horizontal ? 'w-12 h-6' : 'w-6 h-12',
    normal: horizontal ? 'w-16 h-8' : 'w-8 h-16',
  };

  const glowStyle = glowColor
    ? { boxShadow: `0 0 8px 2px ${glowColor}`, borderColor: glowColor }
    : {};

  return (
    <div
      onClick={disabled ? undefined : onClick}
      className={`
            ${sizeClasses[size]}
            bg-amber-50 rounded border flex
            ${horizontal ? 'flex-row' : 'flex-col'}
            ${selected ? 'border-yellow-400 ring-2 ring-yellow-400 scale-105' : glowColor ? '' : 'border-slate-600'}
            ${playable && !disabled ? 'cursor-pointer hover:border-green-400 hover:scale-105' : ''}
            ${onClick && !disabled ? 'cursor-pointer' : ''}
            ${disabled ? 'opacity-50 cursor-not-allowed' : ''}
            transition-all shadow-md
          `}
      style={glowStyle}
    >
      <div className={`flex-1 ${horizontal ? 'border-r' : 'border-b'} border-slate-400`}>
        <DominoPips value={tile.left} size={size === 'tiny' ? 'small' : 'normal'} horizontal={horizontal} />
      </div>
      <div className="flex-1">
        <DominoPips value={tile.right} size={size === 'tiny' ? 'small' : 'normal'} horizontal={horizontal} />
      </div>
    </div>
  );
}

function HiddenTiles({ count, position }: { count: number; position: 'left' | 'right' | 'top' }) {
  const isVertical = position === 'left' || position === 'right';

  return (
    <div className={`flex ${isVertical ? 'flex-col' : 'flex-row'} gap-0.5 items-center justify-center`}>
      {Array.from({ length: Math.min(count, 7) }).map((_, i) => (
        <div
          key={i}
          className={`
                ${isVertical ? 'w-4 h-6' : 'w-6 h-4'}
                bg-slate-700 rounded-sm border border-slate-600
                flex items-center justify-center
              `}
        >
          <div className="w-1 h-1 bg-slate-500 rounded-full" />
        </div>
      ))}
      {count > 7 && (
        <span className="text-slate-400 text-xs ml-1">+{count - 7}</span>
      )}
    </div>
  );
}

function PlayerSeat({
  participant,
  position,
  isMe,
}: {
  participant?: ApiGame['participants'][0];
  position: 'top' | 'bottom' | 'left' | 'right';
  isMe: boolean;
}) {
  const isVertical = position === 'left' || position === 'right';

  if (!participant) {
    return (
      <div className={`flex items-center justify-center p-4 ${isVertical ? 'h-full' : 'w-full'}`}>
        <div className="w-14 h-14 rounded-full bg-slate-700 border-2 border-dashed border-slate-500 flex items-center justify-center">
          <span className="text-slate-500 text-2xl">?</span>
        </div>
      </div>
    );
  }

  const teamLabel = participant.team > 0 ? `T${participant.team}` : '';
  const isCurrentTurn = participant.isCurrentTurn;

  if (isVertical) {
    return (
      <div className={`
        flex flex-col items-center gap-3 p-4 rounded-xl min-w-[140px]
        ${isCurrentTurn ? 'bg-amber-900/50 ring-2 ring-amber-500' : 'bg-slate-800/70'}
      `}>
        <div
          className="w-12 h-12 rounded-full flex items-center justify-center text-white font-bold text-lg border-2 shrink-0"
          style={{ backgroundColor: participant.color, borderColor: isCurrentTurn ? '#fbbf24' : participant.color }}
        >
          {participant.displayName.charAt(0).toUpperCase()}
        </div>

        <div className="flex flex-col items-center text-center">
          <div className="flex items-center gap-1.5 flex-wrap justify-center">
            <span className={`text-sm font-medium ${isMe ? 'text-amber-400' : 'text-white'}`}>
              {participant.displayName}
            </span>
            {teamLabel && (
              <span className="text-xs px-1.5 py-0.5 rounded bg-slate-600 text-slate-300">{teamLabel}</span>
            )}
          </div>
          {isMe && <span className="text-xs text-amber-400/70">(You)</span>}

          <div className="flex items-center gap-3 text-xs text-slate-400 mt-2">
            <span className="font-medium text-white">{participant.totalScore} pts</span>
            <span className="text-slate-500">|</span>
            <span>{participant.tileCount} tiles</span>
          </div>
        </div>

        {!isMe && (
          <div className="mt-2">
            <HiddenTiles count={participant.tileCount} position={position} />
          </div>
        )}
      </div>
    );
  }

  return (
    <div className={`
      flex items-center gap-4 px-6 py-3 rounded-xl
      ${position === 'top' ? 'flex-col' : 'flex-col-reverse'}
      ${isCurrentTurn ? 'bg-amber-900/50 ring-2 ring-amber-500' : 'bg-slate-800/70'}
    `}>
      <div className="flex items-center gap-3">
        <div
          className="w-10 h-10 rounded-full flex items-center justify-center text-white font-bold border-2 shrink-0"
          style={{ backgroundColor: participant.color, borderColor: isCurrentTurn ? '#fbbf24' : participant.color }}
        >
          {participant.displayName.charAt(0).toUpperCase()}
        </div>

        <div className="flex flex-col">
          <div className="flex items-center gap-1.5">
            <span className={`text-sm font-medium ${isMe ? 'text-amber-400' : 'text-white'}`}>
              {participant.displayName}
              {isMe && ' (You)'}
            </span>
            {teamLabel && (
              <span className="text-xs px-1.5 py-0.5 rounded bg-slate-600 text-slate-300">{teamLabel}</span>
            )}
          </div>
          <div className="flex items-center gap-3 text-xs text-slate-400">
            <span className="font-medium text-white">{participant.totalScore} pts</span>
            <span className="text-slate-500">|</span>
            <span>{participant.tileCount} tiles</span>
          </div>
        </div>
      </div>

      {!isMe && (
        <HiddenTiles count={participant.tileCount} position="top" />
      )}
    </div>
  );
}

function GameBoard({ board, deferredPoints }: { board: ApiGame['board']; deferredPoints: number }) {
  const displayTiles: typeof board = [];
  for (const tile of board) {
    if (tile.side === 'left') {
      displayTiles.unshift(tile);
    } else {
      displayTiles.push(tile);
    }
  }

  return (
    <div className="bg-green-900/80 rounded-xl p-3 min-h-32 w-full overflow-x-auto relative">
      {deferredPoints > 0 && (
        <div className="absolute top-2 right-2 bg-amber-600 text-white text-xs px-2 py-1 rounded">
          Deferred: {deferredPoints} pts
        </div>
      )}

      {board.length === 0 ? (
        <div className="flex items-center justify-center h-20">
          <p className="text-green-300/70 italic">Waiting for first tile...</p>
        </div>
      ) : (
        <div className="flex gap-1 items-center justify-center py-2">
          {displayTiles.map((tile, idx) => {
            const isDouble = tile.left === tile.right;
            return (
              <DominoTileView
                key={idx}
                tile={{ left: tile.left, right: tile.right }}
                horizontal={!isDouble}
                size="small"
                glowColor={tile.playedByColor}
              />
            );
          })}
        </div>
      )}
    </div>
  );
}

function Auth({ onLogin }: { onLogin: (token: string, user: User) => void }) {
  const [mode, setMode] = useState<'login' | 'register'>('login');
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [email, setEmail] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);

    try {
      const endpoint = mode === 'login' ? '/auth/login' : '/auth/register';
      const body = mode === 'login'
        ? { userOrEmail: userName, password }
        : { userName, password, displayName: displayName || userName, email: email || null };

      const res = await fetch(`${API_BASE}${endpoint}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });

      const data = await res.json();

      if (!res.ok) {
        setError(data.message || 'Request failed');
        return;
      }

      if (mode === 'register' && data.token) {
        onLogin(data.token, data.user);
      } else if (mode === 'register') {
        setMode('login');
        setError('Registration successful! Please login.');
      } else {
        onLogin(data.token, data.user);
      }
    } catch {
      setError('Network error. Check your connection.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen bg-gradient-to-br from-slate-900 via-slate-800 to-slate-900 flex items-center justify-center p-4">
      <div className="bg-slate-800/90 backdrop-blur rounded-2xl shadow-2xl p-8 w-full max-w-md border border-slate-700">
        <h1 className="text-3xl font-bold text-white mb-2 text-center">🁣 Mode101</h1>
        <p className="text-slate-400 text-center mb-6">Azerbaijani Dominoes</p>

        <div className="flex gap-2 mb-6">
          <button
            onClick={() => setMode('login')}
            className={`flex-1 py-2.5 rounded-lg font-medium transition ${mode === 'login' ? 'bg-cyan-600 text-white' : 'bg-slate-700 text-slate-300 hover:bg-slate-600'}`}
          >
            Login
          </button>
          <button
            onClick={() => setMode('register')}
            className={`flex-1 py-2.5 rounded-lg font-medium transition ${mode === 'register' ? 'bg-cyan-600 text-white' : 'bg-slate-700 text-slate-300 hover:bg-slate-600'}`}
          >
            Register
          </button>
        </div>

        <form onSubmit={handleSubmit} className="space-y-4">
          {mode === 'register' && (
            <>
              <input
                type="text"
                placeholder="Display Name (optional)"
                value={displayName}
                onChange={(e) => setDisplayName(e.target.value)}
                className="w-full p-3 bg-slate-700/50 text-white rounded-lg border border-slate-600 focus:border-cyan-500 focus:ring-1 focus:ring-cyan-500 outline-none transition"
              />
              <input
                type="email"
                placeholder="Email (optional)"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                className="w-full p-3 bg-slate-700/50 text-white rounded-lg border border-slate-600 focus:border-cyan-500 focus:ring-1 focus:ring-cyan-500 outline-none transition"
              />
            </>
          )}
          <input
            type="text"
            placeholder={mode === 'login' ? 'Username or Email' : 'Username'}
            value={userName}
            onChange={(e) => setUserName(e.target.value)}
            className="w-full p-3 bg-slate-700/50 text-white rounded-lg border border-slate-600 focus:border-cyan-500 focus:ring-1 focus:ring-cyan-500 outline-none transition"
            required
          />
          <input
            type="password"
            placeholder="Password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            className="w-full p-3 bg-slate-700/50 text-white rounded-lg border border-slate-600 focus:border-cyan-500 focus:ring-1 focus:ring-cyan-500 outline-none transition"
            required
          />
          {error && (
            <p className={`text-sm px-3 py-2 rounded ${error.includes('successful') ? 'bg-green-900/50 text-green-400' : 'bg-red-900/50 text-red-400'}`}>
              {error}
            </p>
          )}
          <button
            type="submit"
            disabled={loading}
            className="w-full bg-cyan-600 hover:bg-cyan-500 disabled:bg-cyan-800 text-white font-semibold py-3 rounded-lg transition"
          >
            {loading ? 'Please wait...' : mode === 'login' ? 'Login' : 'Register'}
          </button>
        </form>
      </div>
    </div>
  );
}

function Lobby({
  token,
  user,
  onGameSelect,
  onLogout,
}: {
  token: string;
  user: User;
  onGameSelect: (game: ApiGame) => void;
  onLogout: () => void;
}) {
  const [games, setGames] = useState<ApiGame[]>([]);
  const [loading, setLoading] = useState(true);
  const [creating, setCreating] = useState(false);
  const [joinCode, setJoinCode] = useState('');
  const [statusFilter, setStatusFilter] = useState<'all' | 'waiting' | 'voting' | 'active' | 'round_end' | 'finished'>('waiting');
  const [onlyMine, setOnlyMine] = useState(false);
  const [search, setSearch] = useState('');
  const [createModal, setCreateModal] = useState(false);
  const [newGameTeam, setNewGameTeam] = useState(false);
  const [newGameMax, setNewGameMax] = useState(4);
  const [newGameAnonymous, setNewGameAnonymous] = useState(false);

  const fetchGames = useCallback(async () => {
    try {
      const params = new URLSearchParams();
      if (statusFilter !== 'all') params.append('status', statusFilter);
      if (onlyMine) params.append('onlyMine', 'true');
      if (search) params.append('q', search);

      const res = await fetch(`${API_BASE}/games?${params}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (res.ok) {
        const data = await res.json();
        setGames(data);
      }
    } catch {
      console.error('Failed to fetch games');
    } finally {
      setLoading(false);
    }
  }, [token, statusFilter, onlyMine, search]);

  useEffect(() => {
    fetchGames();
    const interval = setInterval(fetchGames, 5000);
    return () => clearInterval(interval);
  }, [fetchGames]);

  const createGame = async () => {

    setCreating(true);
    try {
      const res = await fetch(`${API_BASE}/games`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({
          maxPlayers: newGameMax,
          minPlayers: 2,
          isTeamGame: newGameTeam,
          isAnonymous: newGameAnonymous,
        }),
      });

      if (res.ok) {
        const game = await res.json();
        await joinGame(game.id);
      }
    } catch {
      console.error('Failed to create game');
    } finally {
      setCreating(false);
      setCreateModal(false);
    }
  };

  const joinGame = async (gameId: string) => {
    try {
      const res = await fetch(`${API_BASE}/games/${gameId}/join`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}` },
      });

      if (res.ok) {
        const fullRes = await fetch(`${API_BASE}/games/${gameId}`, {
          headers: { Authorization: `Bearer ${token}` },
        });
        if (fullRes.ok) {
          const game = await fullRes.json();
          onGameSelect(game);
        }
      }
    } catch {
      console.error('Failed to join game');
    }
  };

  const joinByCode = async () => {
    if (!joinCode.trim()) return;
    try {
      const res = await fetch(`${API_BASE}/games/by-code/${joinCode.trim().toUpperCase()}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (res.ok) {
        const { id } = await res.json();
        await joinGame(id);
      }
    } catch {
      console.error('Game not found');
    }
  };

  const openGame = async (gameId: string) => {
    try {
      const res = await fetch(`${API_BASE}/games/${gameId}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (res.ok) {
        const game = await res.json();
        onGameSelect(game);
      }
    } catch {
      console.error('Failed to open game');
    }
  };

  return (
    <div className="min-h-screen bg-gradient-to-br from-slate-900 via-slate-800 to-slate-900 p-4 md:p-8">
      <div className="max-w-6xl mx-auto">
        <div className="flex justify-between items-center mb-6">
          <div>
            <h1 className="text-3xl font-bold text-white mb-1">🁣 Mode101 Lobby</h1>
            <p className="text-slate-400">
              Welcome,
              <span className="ml-2 font-semibold text-amber-300 drop-shadow-[0_0_6px_rgba(251,191,36,0.35)]">
                {user.displayName || user.email}
              </span>
            </p>
          </div>
          <button
            onClick={onLogout}
            className="px-4 py-2 bg-slate-700 hover:bg-slate-600 text-white rounded-lg transition"
          >
            Logout
          </button>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mb-6">
          <div className="bg-slate-800/80 backdrop-blur rounded-xl p-4 border border-slate-700">
            <h2 className="text-lg font-semibold text-white mb-3 flex items-center gap-2">
              <Users size={20} /> Quick Actions
            </h2>
            <div className="flex flex-col gap-3">
              <button
                onClick={() => setCreateModal(true)}
                disabled={creating}
                className="w-full bg-cyan-600 hover:bg-cyan-500 disabled:bg-cyan-800 text-white font-semibold py-3 rounded-lg transition"
              >
                Create New Game
              </button>
              <div className="flex gap-2">
                <input
                  type="text"
                  placeholder="Game Code"
                  value={joinCode}
                  onChange={(e) => setJoinCode(e.target.value.toUpperCase())}
                  className="flex-1 p-3 bg-slate-700/50 text-white rounded-lg border border-slate-600 focus:border-cyan-500 outline-none uppercase"
                  maxLength={6}
                />
                <button
                  onClick={joinByCode}
                  className="px-6 bg-green-600 hover:bg-green-500 text-white font-semibold rounded-lg transition"
                >
                  Join
                </button>
              </div>
            </div>
          </div>

          <div className="bg-slate-800/80 backdrop-blur rounded-xl p-4 border border-slate-700">
            <h2 className="text-lg font-semibold text-white mb-3">Filters</h2>
            <div className="flex flex-col gap-3">
              <div className="flex gap-2">
                <input
                  type="text"
                  placeholder="Search code or player..."
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  className="flex-1 p-3 bg-slate-700/50 text-white rounded-lg border border-slate-600 focus:border-cyan-500 outline-none"
                />
                <button
                  onClick={() => { setSearch(''); setStatusFilter('all'); setOnlyMine(false); }}
                  className="px-3 bg-slate-700 hover:bg-slate-600 rounded-lg text-slate-300"
                >
                  <X size={18} />
                </button>
              </div>
              <div className="flex gap-2 items-center">
                <select
                  value={statusFilter}
                  onChange={(e) => setStatusFilter(e.target.value as typeof statusFilter)}
                  className="flex-1 p-2.5 bg-slate-700/50 text-white rounded-lg border border-slate-600 focus:border-cyan-500 outline-none"
                >
                  <option value="all">All Status</option>
                  <option value="waiting">Waiting</option>
                  <option value="voting">Voting</option>
                  <option value="active">Active</option>
                  <option value="round_end">Round End</option>
                  <option value="finished">Finished</option>
                </select>
                <label className="flex items-center gap-2 text-slate-300 whitespace-nowrap">
                  <input
                    type="checkbox"
                    checked={onlyMine}
                    onChange={(e) => setOnlyMine(e.target.checked)}
                    className="accent-cyan-500 w-4 h-4"
                  />
                  My games
                </label>
              </div>
            </div>
          </div>
        </div>

        <div className="bg-slate-800/80 backdrop-blur rounded-xl p-4 border border-slate-700">
          <h2 className="text-lg font-semibold text-white mb-4">Games</h2>

          {loading ? (
            <p className="text-slate-400 text-center py-8">Loading...</p>
          ) : games.length === 0 ? (
            <p className="text-slate-400 text-center py-8 italic">No games found</p>
          ) : (
            <div className="space-y-2">
              {games.map((g) => {
                const iAmIn = g.participants.some((p) => p.userId === user.id || p.displayName === user.displayName);
                const isFull = g.participants.length >= g.maxPlayers;

                const statusColors: Record<string, string> = {
                  waiting: 'text-green-400',
                  voting: 'text-yellow-400',
                  active: 'text-cyan-400',
                  round_end: 'text-orange-400',
                  finished: 'text-purple-400',
                };

                let actionLabel = 'Join';
                let buttonClass = 'bg-green-600 hover:bg-green-500';

                if (g.status === 'finished') {
                  actionLabel = 'View';
                  buttonClass = 'bg-purple-600 hover:bg-purple-500';
                } else if (iAmIn) {
                  actionLabel = 'Resume';
                  buttonClass = 'bg-cyan-600 hover:bg-cyan-500';
                } else if (isFull) {
                  actionLabel = 'Watch';
                  buttonClass = 'bg-slate-600 hover:bg-slate-500';
                }

                return (
                  <div
                    key={g.id}
                    className="bg-slate-700/50 rounded-lg p-4 flex flex-col md:flex-row md:items-center justify-between gap-3 hover:bg-slate-700 transition"
                  >
                    <div className="flex-1">
                      <div className="flex items-center gap-3 mb-1">
                        <span className="text-white font-bold text-lg">{g.code}</span>
                        <span className={`text-sm ${statusColors[g.status] || 'text-slate-400'}`}>
                          {g.status}
                        </span>
                        {g.isTeamGame && (
                          <span className="text-xs px-2 py-0.5 bg-amber-600/30 text-amber-400 rounded">
                            Teams
                          </span>
                        )}
                        <span className="text-slate-500 text-sm">
                          R{g.roundNumber}
                        </span>
                      </div>
                      <div className="flex items-center gap-2 text-sm text-slate-400">
                        <span>{g.participants.length}/{g.maxPlayers} players</span>
                        {g.participants.length > 0 && (
                          <>
                            <span>•</span>
                            <span>{g.participants.map((p) => p.displayName).join(', ')}</span>
                          </>
                        )}
                      </div>
                      {g.isTeamGame && g.status !== 'waiting' && (
                        <div className="flex gap-4 mt-1 text-xs">
                          <span className="text-red-400">Team 1: {g.team1Score}</span>
                          <span className="text-cyan-400">Team 2: {g.team2Score}</span>
                        </div>
                      )}
                    </div>
                    <button
                      onClick={() => iAmIn ? openGame(g.id) : joinGame(g.id)}
                      className={`px-5 py-2 text-white font-medium rounded-lg transition ${buttonClass}`}
                    >
                      {actionLabel}
                    </button>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>

      {createModal && (
        <div className="fixed inset-0 bg-black/60 flex items-center justify-center p-4 z-50">
          <div className="bg-slate-800 rounded-xl p-6 w-full max-w-md border border-slate-700">
            <h2 className="text-xl font-bold text-white mb-4">Create Game</h2>

            <div className="space-y-4">
              <div>
                <label className="block text-slate-300 text-sm mb-2">Max Players</label>
                <div className="flex gap-2">
                  {[2, 3, 4].map((n) => (
                    <button
                      key={n}
                      onClick={() => { setNewGameMax(n); if (n !== 4) setNewGameTeam(false); }}
                      className={`flex-1 py-2 rounded-lg font-medium transition ${newGameMax === n ? 'bg-cyan-600 text-white' : 'bg-slate-700 text-slate-300 hover:bg-slate-600'}`}
                    >
                      {n}
                    </button>
                  ))}
                </div>
              </div>

              <div>
                <label className="flex items-center gap-3 text-slate-300">
                  <input
                    type="checkbox"
                    checked={newGameTeam}
                    onChange={(e) => setNewGameTeam(e.target.checked)}
                    disabled={newGameMax !== 4}
                    className="accent-cyan-500 w-5 h-5"
                  />
                  Team Game (4 players only)
                </label>
                {newGameTeam && (
                  <p className="text-xs text-slate-500 mt-1 ml-8">
                    Teams: Position 0+2 vs 1+3
                  </p>
                )}
              </div>
            </div>

            <div>
              <label className="flex items-center gap-3 text-slate-300">
                <input
                  type="checkbox"
                  checked={newGameAnonymous}
                  onChange={(e) => setNewGameAnonymous(e.target.checked)}
                  className="accent-cyan-500 w-5 h-5"
                />
                Anonymous Mode
              </label>
              <p className="text-xs text-slate-500 mt-1 ml-8">
                Names hidden until game ends
              </p>
            </div>

            <div className="flex gap-3 mt-6">
              <button
                onClick={() => setCreateModal(false)}
                className="flex-1 py-2.5 bg-slate-700 hover:bg-slate-600 text-white rounded-lg transition"
              >
                Cancel
              </button>
              <button
                onClick={createGame}
                disabled={creating}
                className="flex-1 py-2.5 bg-cyan-600 hover:bg-cyan-500 disabled:bg-cyan-800 text-white font-semibold rounded-lg transition"
              >
                {creating ? 'Creating...' : 'Create'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function DominoGame({
  token,
  user,
  initialGame,
  onBack,
}: {
  token: string;
  user: User;
  initialGame: ApiGame;
  onBack: () => void;
}) {
  const [game, setGame] = useState<ApiGame>(initialGame);
  const [selectedTile, setSelectedTile] = useState<GL.Tile | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [newlyDrawnTiles, setNewlyDrawnTiles] = useState<GL.Tile[]>([]);
  const [countdown, setCountdown] = useState<number | null>(null);
  const { playSound, isMuted, toggleMute } = useSound();
  const [muted, setMuted] = useState(isMuted());

  const handleToggleMute = () => {
    const newMuted = toggleMute();
    setMuted(newMuted);
  };

  useEffect(() => {
    if (game.status === 'round_end' && countdown === null) {
      setCountdown(5);
    }
    if (game.status !== 'round_end') {
      setCountdown(null);
    }
  }, [game.status]);

  useEffect(() => {
    if (countdown === null || countdown <= 0) return;

    const timer = setTimeout(() => {
      setCountdown(c => (c ?? 1) - 1);
    }, 1000);

    return () => clearTimeout(timer);
  }, [countdown]);

  useEffect(() => {
    if (countdown === 0 && game.status === 'round_end') {
      startNextRound();
    }
  }, [countdown, game.status]);

  const myParticipant = useMemo(() => {
    return game.participants.find((p) => p.userId === user.id);
  }, [game.participants, user.id]);

  const myPosition = myParticipant?.position ?? null;
  const isMyTurn = myParticipant?.isCurrentTurn ?? false;

  const prevIsMyTurn = useRef(isMyTurn);
  const prevStatus = useRef(game.status);
  const prevBoardLength = useRef(game.board.length);
  const prevBoneyardCount = useRef(game.boneyardCount);

  const refreshGame = useCallback(async () => {
    try {
      const res = await fetch(`${API_BASE}/games/${game.id}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (res.ok) {
        const data = await res.json();
        setGame(data);
      }
    } catch {
      console.error('Failed to refresh game');
    }
  }, [game.id, token]);

  useEffect(() => {
    const interval = setInterval(refreshGame, 2000);
    return () => clearInterval(interval);
  }, [refreshGame]);

  useEffect(() => {
    setNewlyDrawnTiles([]);
    setSelectedTile(null);
  }, [game.roundNumber]);

  useEffect(() => {
    if (isMyTurn && !prevIsMyTurn.current && game.status === 'active') {
      playSound('your_turn');
    }
    prevIsMyTurn.current = isMyTurn;
  }, [isMyTurn, game.status, playSound]);

  useEffect(() => {
    if (game.status === 'round_end' && prevStatus.current === 'active') {
      const dominoPlayer = game.allHands?.find(h => h.hand.length === 0);
      if (dominoPlayer) {
        playSound('domino');
      } else {
        playSound('round_end');
      }
    }
    prevStatus.current = game.status;
  }, [game.status, game.allHands, playSound]);

  useEffect(() => {
    if (game.board.length > prevBoardLength.current && game.board.length > 0) {
      const lastTile = game.board[game.board.length - 1];
      const isDouble = lastTile.left === lastTile.right;

      if (isDouble) {
        const soundFile = `/sounds/double_${lastTile.left}.mp3`;
        playSound('double_play', { file: soundFile, volume: 0.5 });
      } else {
        playSound('tile_play');
      }
    }
    prevBoardLength.current = game.board.length;
  }, [game.board, playSound]);

  useEffect(() => {
    const boneyardDecreased = game.boneyardCount < prevBoneyardCount.current;
    const boardSame = game.board.length === prevBoardLength.current;

    if (boneyardDecreased && boardSame && game.status === 'active') {
      playSound('tile_draw');
    }
    prevBoneyardCount.current = game.boneyardCount;
  }, [game.boneyardCount, game.board.length, game.status, playSound]);

  const getRelativePosition = useCallback((targetPosition: number): 'bottom' | 'left' | 'top' | 'right' => {
    if (myPosition === null) return 'bottom';

    const playerCount = game.participants.length;
    const diff = (targetPosition - myPosition + playerCount) % playerCount;

    if (playerCount === 2) {
      return diff === 0 ? 'bottom' : 'top';
    }

    if (playerCount === 3) {
      if (diff === 0) return 'bottom';
      if (diff === 1) return 'right';
      return 'left';
    }

    if (diff === 0) return 'bottom';
    if (diff === 1) return 'right';
    if (diff === 2) return 'top';
    return 'left';
  }, [myPosition, game.participants.length]);

  const sortedParticipants = useMemo(() => {
    const positions: ('bottom' | 'left' | 'top' | 'right')[] = ['bottom', 'right', 'top', 'left'];
    const result: { position: 'bottom' | 'left' | 'top' | 'right'; participant?: ApiGame['participants'][0] }[] = [];

    for (const pos of positions) {
      const p = game.participants.find((part) => getRelativePosition(part.position) === pos);
      result.push({ position: pos, participant: p });
    }

    return result;
  }, [game.participants, getRelativePosition]);

  const myHand = useMemo((): GL.Tile[] => {
    return game.myHand.map((t) => ({ left: t[0], right: t[1] }));
  }, [game.myHand]);

  const canPlayTile = useCallback((tile: GL.Tile): { left: boolean; right: boolean } => {
    if (!isMyTurn || game.status !== 'active') return { left: false, right: false };

    const isFirstTile = game.board.length === 0;
    const isFirstRound = game.roundNumber === 1;

    if (isFirstTile && isFirstRound) {
      if (game.requiredFirstTile) {
        const matches = tile.left === game.requiredFirstTile[0] && tile.right === game.requiredFirstTile[1];
        return { left: matches, right: matches };
      }
      const isDouble = tile.left === tile.right && tile.left >= 1;
      return { left: isDouble, right: isDouble };
    }

    if (isFirstTile) {
      return { left: true, right: true };
    }

    const canLeft = tile.left === game.boardLeft || tile.right === game.boardLeft;
    const canRight = tile.left === game.boardRight || tile.right === game.boardRight;

    return { left: canLeft, right: canRight };
  }, [isMyTurn, game.status, game.board.length, game.roundNumber, game.boardLeft, game.boardRight, game.requiredFirstTile]);

  const hasPlayableTile = useMemo(() => {
    return myHand.some((t) => {
      const { left, right } = canPlayTile(t);
      return left || right;
    });
  }, [myHand, canPlayTile]);

  const playTile = async (side: 'left' | 'right') => {
    if (!selectedTile || !isMyTurn) return;

    setLoading(true);
    setError(null);

    try {
      const res = await fetch(`${API_BASE}/games/${game.id}/play`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({
          tileLeft: selectedTile.left,
          tileRight: selectedTile.right,
          side,
        }),
      });

      if (res.ok) {
        setSelectedTile(null);
        setNewlyDrawnTiles([]);
        await refreshGame();
      }
    } catch {
      setError('Network error');
    } finally {
      setLoading(false);
    }
  };

  const drawOrPass = async () => {
    if (!isMyTurn) return;

    setLoading(true);
    setError(null);

    try {
      const res = await fetch(`${API_BASE}/games/${game.id}/draw`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}` },
      });

      if (res.ok) {
        const data = await res.json();
        if (data.passed) {
          setNewlyDrawnTiles([]);
        } else if (data.drawnTiles && data.drawnTiles.length > 0) {
          const drawn = data.drawnTiles.map((t: number[]) => ({ left: t[0], right: t[1] }));
          setNewlyDrawnTiles(drawn);
        }
        await refreshGame();
      }
      else {
        const data = await res.json();
        setError(data.message || 'Failed to draw/pass');
      }
    } catch {
      setError('Network error');
    } finally {
      setLoading(false);
    }
  };

  const voteToStart = async () => {
    setLoading(true);
    try {
      await fetch(`${API_BASE}/games/${game.id}/vote-start`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}` },
      });
      await refreshGame();
    } catch {
      console.error('Failed to vote');
    } finally {
      setLoading(false);
    }
  };

  const startNextRound = async () => {
    setLoading(true);
    try {
      await fetch(`${API_BASE}/games/${game.id}/start-round`, {
        method: 'POST',
        headers: { Authorization: `Bearer ${token}` },
      });
      await refreshGame();
    } catch {
      console.error('Failed to start round');
    } finally {
      setLoading(false);
    }
  };

  const playableInfo = selectedTile ? canPlayTile(selectedTile) : { left: false, right: false };

  const topPlayer = sortedParticipants.find((s) => s.position === 'top');
  const leftPlayer = sortedParticipants.find((s) => s.position === 'left');
  const rightPlayer = sortedParticipants.find((s) => s.position === 'right');

  return (
    <div className="min-h-screen bg-gradient-to-br from-slate-900 via-slate-800 to-slate-900 flex flex-col">
      <div className="bg-slate-800/90 backdrop-blur border-b border-slate-700 px-4 py-2">
        <div className="flex items-center justify-between">
          <button
            onClick={onBack}
            className="px-3 py-1.5 bg-slate-700 hover:bg-slate-600 text-white rounded-lg text-sm transition"
          >
            ← Lobby
          </button>

          <button
            onClick={handleToggleMute}
            className="px-3 py-1.5 bg-slate-700 hover:bg-slate-600 text-white rounded-lg text-sm transition"
            title={muted ? 'Unmute' : 'Mute'}
          >
            {muted ? <VolumeX size={18} /> : <Volume2 size={18} />}
          </button>

          {(game.status === 'waiting' || game.status === 'voting') && !myParticipant?.hasVotedToStart && (
            <button
              onClick={async () => {
                if (confirm('Leave this game?')) {
                  await fetch(`${API_BASE}/games/${game.id}/leave`, {
                    method: 'POST',
                    headers: { Authorization: `Bearer ${token}` },
                  });
                  onBack();
                }
              }}
              className="px-3 py-1.5 bg-red-700 hover:bg-red-600 text-white rounded-lg text-sm transition"
            >
              Leave
            </button>
          )}

          <div className="flex items-center gap-6">
            <div className="flex items-center gap-2">
              <span className="text-slate-400 text-sm">Game</span>
              <span className="text-white font-bold">{game.code}</span>
            </div>

            <div className="flex items-center gap-2">
              <Layers size={16} className="text-slate-400" />
              <span className="text-white">Round {game.roundNumber}</span>
            </div>

            <div className="flex items-center gap-2 text-sm text-slate-300">
              <span className="text-slate-500">Boneyard:</span>
              <span className="font-medium">{game.boneyardCount}</span>
              {game.participants.length < 4 ? (
                <span className="text-slate-500">
                  ({game.playableBoneyardCount} playable, draw {game.participants.length === 2 ? 2 : 1})
                </span>
              ) : (
                <span className="text-slate-500">(no boneyard in 4p)</span>
              )}
            </div>

            {game.isTeamGame && (
              <div className="flex items-center gap-4">
                <div className="flex items-center gap-1">
                  <div className="w-3 h-3 rounded-full bg-red-500" />
                  <span className="text-white text-sm">{game.team1Score}</span>
                </div>
                <span className="text-slate-500">vs</span>
                <div className="flex items-center gap-1">
                  <div className="w-3 h-3 rounded-full bg-cyan-500" />
                  <span className="text-white text-sm">{game.team2Score}</span>
                </div>
              </div>
            )}

            <div className="flex items-center gap-2">
              <span className={`px-2 py-1 rounded text-xs font-medium ${game.status === 'active' ? 'bg-green-600 text-white' :
                game.status === 'waiting' ? 'bg-yellow-600 text-white' :
                  game.status === 'voting' ? 'bg-amber-600 text-white' :
                    game.status === 'round_end' ? 'bg-orange-600 text-white' :
                      'bg-purple-600 text-white'
                }`}>
                {game.status.replace('_', ' ').toUpperCase()}
              </span>
            </div>

            <div className="bg-slate-900/75 rounded-lg px-3 py-2 text-sm">
              {game.isTeamGame ? (
                <div className="flex flex-col gap-1">
                  <div className="flex items-center justify-between gap-4">
                    <span className="text-slate-400">T1</span>
                    <span className="text-white font-bold">{game.team1Score}</span>
                  </div>
                  <div className="flex items-center justify-between gap-4">
                    <span className="text-slate-400">T2</span>
                    <span className="text-white font-bold">{game.team2Score}</span>
                  </div>
                </div>
              ) : (
                <div className="flex flex-col gap-0.5">
                  {[...game.participants]
                    .sort((a, b) => b.totalScore - a.totalScore)
                    .map((p) => (
                      <div key={p.userId} className="flex items-center justify-between gap-3">
                        <span
                          className="text-xs truncate max-w-16"
                          style={{ color: p.color }}
                        >
                          {p.displayName}
                        </span>
                        <span className="text-white font-medium text-xs">{p.totalScore}</span>
                      </div>
                    ))}
                </div>
              )}
            </div>
          </div>

          <div className="w-20" />
        </div>
      </div>

      {error && (
        <div className="bg-red-900/80 text-red-200 px-4 py-2 text-center text-sm">
          {error}
          <button onClick={() => setError(null)} className="ml-4 underline">Dismiss</button>
        </div>
      )}

      <div className="flex-1 flex flex-col p-4 w-full">
        <div className="flex justify-center mb-2">
          <PlayerSeat
            participant={topPlayer?.participant}
            position="top"
            isMe={topPlayer?.participant?.userId === user.id}
          />
        </div>

        <div className="flex-1 flex items-stretch gap-4 min-h-0">
          <div className="flex items-center justify-center w-44 shrink-0">
            <PlayerSeat
              participant={leftPlayer?.participant}
              position="left"
              isMe={leftPlayer?.participant?.userId === user.id}
            />
          </div>

          <div className="flex-1 flex flex-col p-4 w-full">
            <GameBoard board={game.board} deferredPoints={game.deferredPoints ?? 0} />

            <div className="flex justify-center gap-4 mt-2 text-xs">
              <span className="px-2 py-1 bg-slate-700 rounded text-amber-400">
                ← {game.boardLeft ?? '?'}
              </span>
              <span className="px-2 py-1 bg-slate-700 rounded text-amber-400">
                {game.boardRight ?? '?'} →
              </span>
            </div>
          </div>

          <div className="flex items-center justify-center w-44 shrink-0">
            <PlayerSeat
              participant={rightPlayer?.participant}
              position="right"
              isMe={rightPlayer?.participant?.userId === user.id}
            />
          </div>
        </div>

        <div className="mt-4">
          {game.status === 'waiting' && (
            <div className="bg-slate-800/80 rounded-xl p-6 text-center">
              <p className="text-slate-300 mb-4">
                Waiting for players... ({game.participants.length}/{game.maxPlayers})
              </p>
              {game.participants.length >= game.minPlayers && !myParticipant?.hasVotedToStart && (
                <button
                  onClick={voteToStart}
                  disabled={loading}
                  className="px-6 py-3 bg-green-600 hover:bg-green-500 text-white font-semibold rounded-lg transition"
                >
                  Vote to Start
                </button>
              )}
              {myParticipant?.hasVotedToStart && (
                <p className="text-green-400">You voted to start. Waiting for others...</p>
              )}
            </div>
          )}

          {game.status === 'voting' && (
            <div className="bg-slate-800/80 rounded-xl p-6 text-center">
              <p className="text-slate-300 mb-4">
                Votes: {game.participants.filter((p) => p.hasVotedToStart).length}/{game.participants.length}
              </p>
              {!myParticipant?.hasVotedToStart && (
                <button
                  onClick={voteToStart}
                  disabled={loading}
                  className="px-6 py-3 bg-green-600 hover:bg-green-500 text-white font-semibold rounded-lg transition"
                >
                  Vote to Start
                </button>
              )}
              {myParticipant?.hasVotedToStart && (
                <p className="text-green-400">Waiting for others to vote...</p>
              )}
            </div>
          )}

          {game.status === 'round_end' && (
            <div className="bg-slate-800/80 rounded-xl p-6 text-center">
              <p className="text-white text-lg font-semibold mb-2">
                Round {game.roundNumber} Complete
              </p>
              {game.allHands && (
                <div className="mt-4 space-y-3">
                  <p className="text-slate-400 text-sm">Remaining tiles:</p>
                  <div className="flex flex-wrap justify-center gap-4">
                    {game.allHands.map((ph) => {
                      const participant = game.participants.find(p => p.position === ph.position);
                      const handSum = ph.hand.reduce((sum, t) => sum + t[0] + t[1], 0);
                      return (
                        <div key={ph.userId} className="bg-slate-700/50 rounded-lg p-3">
                          <div className="flex items-center gap-2 mb-2">
                            <span style={{ color: participant?.color }} className="font-medium text-sm">
                              {participant?.displayName}
                            </span>
                            <span className="text-slate-400 text-xs">({handSum} pts)</span>
                          </div>
                          <div className="flex gap-1">
                            {ph.hand.length === 0 ? (
                              <span className="text-green-400 text-xs">DOMINO!</span>
                            ) : (
                              ph.hand.map((t, i) => (
                                <DominoTileView key={i} tile={{ left: t[0], right: t[1] }} size="tiny" />
                              ))
                            )}
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </div>
              )}
              <div className="mt-4">
                <p className="text-amber-400 text-lg">
                  Next round in <span className="font-bold text-2xl">{countdown ?? 0}</span>s
                </p>
              </div>
            </div>
          )}

          {game.status === 'finished' && (
            <div className="bg-slate-800/80 rounded-xl p-6 text-center">
              <Trophy className="mx-auto mb-2 text-amber-400" size={40} />
              <p className="text-white text-xl font-bold mb-2">Game Over!</p>
              <p className="text-slate-300">{game.reason}</p>
            </div>
          )}

          {game.status === 'active' && myParticipant && (
            <div className="bg-slate-800/80 rounded-xl p-4">
              <div className="flex items-center justify-between mb-3">
                <div className="flex items-center gap-3">
                  <h3 className="text-white font-semibold">Your Hand</h3>
                  {isMyTurn ? (
                    <span className="px-2 py-1 bg-green-600 text-white text-xs rounded animate-pulse">
                      Your Turn!
                    </span>
                  ) : (
                    <span className="text-slate-400 text-sm">
                      Waiting for {game.participants.find((p) => p.isCurrentTurn)?.displayName}...
                    </span>
                  )}
                </div>

                {isMyTurn && !hasPlayableTile && game.playableBoneyardCount > 0 && (
                  <button
                    onClick={drawOrPass}
                    disabled={loading}
                    className="px-4 py-2 bg-amber-600 hover:bg-amber-500 text-white rounded-lg flex items-center gap-2 text-sm transition animate-pulse"
                  >
                    <RotateCcw size={16} />
                    Draw {game.participants.length === 2 ? 2 : 1} ({game.playableBoneyardCount} available)
                  </button>
                )}
              </div>

              <div className="flex gap-2 flex-wrap justify-center">
                {myHand.map((tile, idx) => {
                  const { left: canLeft, right: canRight } = canPlayTile(tile);
                  const playable = canLeft || canRight;
                  const isSelected = selectedTile && GL.tilesEqual(selectedTile, tile);
                  const isNewlyDrawn = newlyDrawnTiles.some(t => GL.tilesEqual(t, tile));

                  return (
                    <div key={idx} className="relative">
                      {isNewlyDrawn && (
                        <div className="absolute -top-2 left-1/2 -translate-x-1/2 bg-cyan-500 text-white text-[10px] px-1.5 rounded-full z-10">
                          NEW
                        </div>
                      )}
                      <DominoTileView
                        tile={tile}
                        selected={!!isSelected}
                        playable={playable}
                        onClick={() => {
                          if (isMyTurn && playable) {
                            setSelectedTile(isSelected ? null : tile);
                          }
                        }}
                        disabled={!isMyTurn || !playable}
                        size="normal"
                        glowColor={isNewlyDrawn ? '#06b6d4' : undefined}
                      />
                    </div>
                  );
                })}
              </div>

              {selectedTile && (
                <div className="mt-4 flex gap-3 justify-center">
                  <button
                    onClick={() => playTile('left')}
                    disabled={!playableInfo.left || loading}
                    className={`px-6 py-2.5 rounded-lg font-semibold transition ${playableInfo.left
                      ? 'bg-green-600 hover:bg-green-500 text-white'
                      : 'bg-slate-700 text-slate-500 cursor-not-allowed'
                      }`}
                  >
                    ← Play Left
                  </button>
                  <button
                    onClick={() => playTile('right')}
                    disabled={!playableInfo.right || loading}
                    className={`px-6 py-2.5 rounded-lg font-semibold transition ${playableInfo.right
                      ? 'bg-green-600 hover:bg-green-500 text-white'
                      : 'bg-slate-700 text-slate-500 cursor-not-allowed'
                      }`}
                  >
                    Play Right →
                  </button>
                  <button
                    onClick={() => setSelectedTile(null)}
                    className="px-6 py-2.5 bg-slate-700 hover:bg-slate-600 text-white rounded-lg transition"
                  >
                    Cancel
                  </button>
                </div>
              )}

              {isMyTurn && !hasPlayableTile && game.playableBoneyardCount === 0 && (
                <div className="text-center mt-3">
                  <p className="text-amber-400 text-sm mb-2">
                    No playable tiles and boneyard exhausted.
                  </p>
                  <button
                    onClick={drawOrPass}
                    disabled={loading}
                    className="px-4 py-2 bg-slate-600 hover:bg-slate-500 text-white rounded-lg text-sm transition"
                  >
                    {loading ? 'Passing...' : 'Pass Turn'}
                  </button>
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

export default function App() {
  const [token, setToken] = useState<string | null>(() => localStorage.getItem('token'));
  const [user, setUser] = useState<User | null>(() => {
    const stored = localStorage.getItem('user');
    return stored ? JSON.parse(stored) : null;
  });
  const [currentGame, setCurrentGame] = useState<ApiGame | null>(null);

  const handleLogin = (newToken: string, newUser: User) => {
    setToken(newToken);
    setUser(newUser);
    localStorage.setItem('token', newToken);
    localStorage.setItem('user', JSON.stringify(newUser));
  };

  const handleLogout = () => {
    setToken(null);
    setUser(null);
    setCurrentGame(null);
    localStorage.removeItem('token');
    localStorage.removeItem('user');
  };

  if (!token || !user) {
    return <Auth onLogin={handleLogin} />;
  }

  if (currentGame) {
    return (
      <DominoGame
        token={token}
        user={user}
        initialGame={currentGame}
        onBack={() => setCurrentGame(null)}
      />
    );
  }

  return (
    <Lobby
      token={token}
      user={user}
      onGameSelect={setCurrentGame}
      onLogout={handleLogout}
    />
  );
}