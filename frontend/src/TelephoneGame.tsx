import { useState, useEffect, useMemo, useCallback, useRef } from 'react';
import { RotateCcw, Trophy, Layers, Volume2, VolumeX } from 'lucide-react';
import { useSound } from './hooks/useSound';

interface TelephoneTile {
  left: number;
  right: number;
  position: string;
  attachedToDoubleIndex?: number;
  attachedSide?: string;
  playedById?: string;
  playedByPosition?: number;
  playedByColor?: string;
  isFlipped?: boolean;
}

interface TelephoneDouble {
  value: number;
  tileIndex: number;
  isClosed: boolean;
  topEnd?: number;
  bottomEnd?: number;
  hasTop: boolean;
  hasBottom: boolean;
}

interface TelephoneBoard {
  leftEnd: number | null;
  rightEnd: number | null;
  tiles: TelephoneTile[];
  telephones: TelephoneDouble[];
}

export interface TelephoneGame {
  id: string;
  code: string;
  mode: string;
  status: string;
  maxPlayers: number;
  minPlayers: number;
  roundNumber: number;
  currentTurn: number;
  boardLeft: number | null;
  boardRight: number | null;
  outcome?: string;
  reason?: string;
  winnerId?: string;
  boneyardCount: number;
  playableBoneyardCount: number;
  boardPoints: number;
  board: TelephoneBoard;
  participants: {
    userId: string;
    displayName: string;
    position: number;
    color: string;
    totalScore: number;
    roundScore: number;
    tileCount: number;
    isCurrentTurn: boolean;
    hasVotedToStart: boolean;
  }[];
  myHand: number[][];
  allHands?: {
    userId: string;
    position: number;
    hand: number[][];
  }[] | null;
  myPosition?: number;
  requiredFirstTile?: number[] | null;
  comboAvailable: boolean;
  comboTiles?: number[][][];
}

interface User {
  id: string;
  userName: string;
  email: string;
  displayName: string;
}

const API_BASE = 'http://172.22.111.136:8000/api/telephone';

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
      {positions[value]?.map(([x, y], idx) => (
        <div
          key={idx}
          className={`absolute ${pipSize} bg-slate-900 rounded-full`}
          style={{ left: `${x}%`, top: `${y}%`, transform: 'translate(-50%, -50%)' }}
        />
      ))}
    </div>
  );
}

function TelephoneTileView({
  tile,
  horizontal = false,
  selected = false,
  playable = false,
  onClick,
  size = 'normal',
  glowColor,
  disabled = false,
  comboHighlight = false,
  flipped = false,
}: {
  tile: { left: number; right: number };
  horizontal?: boolean;
  selected?: boolean;
  playable?: boolean;
  onClick?: () => void;
  size?: 'tiny' | 'small' | 'normal';
  glowColor?: string;
  disabled?: boolean;
  comboHighlight?: boolean;
  flipped?: boolean;
}) {
  const sizeClasses = {
    tiny: horizontal ? 'w-10 h-5' : 'w-5 h-10',
    small: horizontal ? 'w-12 h-6' : 'w-6 h-12',
    normal: horizontal ? 'w-16 h-8' : 'w-8 h-16',
  };

  const glowStyle = glowColor
    ? { boxShadow: `0 0 8px 2px ${glowColor}`, borderColor: glowColor }
    : comboHighlight
      ? { boxShadow: '0 0 12px 3px #22c55e', borderColor: '#22c55e' }
      : {};

  const first = flipped ? tile.right : tile.left;
  const second = flipped ? tile.left : tile.right;

  return (
    <div
      onClick={disabled ? undefined : onClick}
      className={`
        ${sizeClasses[size]}
        bg-amber-50 rounded border flex
        ${horizontal ? 'flex-row' : 'flex-col'}
        ${selected ? 'border-yellow-400 ring-2 ring-yellow-400 scale-105' : glowColor || comboHighlight ? '' : 'border-slate-600'}
        ${playable && !disabled ? 'cursor-pointer hover:border-green-400 hover:scale-105' : ''}
        ${onClick && !disabled ? 'cursor-pointer' : ''}
        ${disabled ? 'opacity-50 cursor-not-allowed' : ''}
        transition-all shadow-md
      `}
      style={glowStyle}
    >
      <div className={`flex-1 ${horizontal ? 'border-r' : 'border-b'} border-slate-400`}>
        <DominoPips value={first} size={size === 'tiny' ? 'small' : 'normal'} horizontal={horizontal} />
      </div>
      <div className="flex-1">
        <DominoPips value={second} size={size === 'tiny' ? 'small' : 'normal'} horizontal={horizontal} />
      </div>
    </div>
  );
}

function TelephoneBoardView({ board, boardPoints }: { board: TelephoneBoard; boardPoints: number }) {
  if (board.tiles.length === 0) {
    return (
      <div className="bg-green-900/80 rounded-xl p-6 min-h-[280px] flex items-center justify-center">
        <p className="text-green-300/70 italic">Waiting for first tile...</p>
      </div>
    );
  }

  const centerTile = board.tiles.find(t => t.position === 'center');
  const leftTiles = board.tiles.filter(t => t.position === 'left' && t.attachedToDoubleIndex == null);
  const rightTiles = board.tiles.filter(t => t.position === 'right' && t.attachedToDoubleIndex == null);

  const activeTelephone = board.telephones.find(t => t.isClosed);

  const getExtensionChain = (telephoneIndex: number, side: 'top' | 'bottom'): TelephoneTile[] => {
    const chain: TelephoneTile[] = [];
    const extensionTiles = board.tiles.filter(t =>
      t.attachedToDoubleIndex === telephoneIndex && t.attachedSide === side
    );
    chain.push(...extensionTiles);
    return chain;
  };

  const renderExtensionChain = (tiles: TelephoneTile[], direction: 'up' | 'down') => {
    if (tiles.length === 0) return null;

    const orderedTiles = direction === 'up' ? [...tiles].reverse() : tiles;

    return (
      <div className={`flex flex-col items-center gap-0.5 ${direction === 'up' ? 'flex-col-reverse' : ''}`}>
        {orderedTiles.map((tile, idx) => {
          const isDouble = tile.left === tile.right;
          return (
            <TelephoneTileView
              key={idx}
              tile={{ left: tile.left, right: tile.right }}
              horizontal={isDouble}
              size="small"
              glowColor={tile.playedByColor}
              flipped={tile.isFlipped}
            />
          );
        })}
      </div>
    );
  };

  const renderTileWithExtensions = (tile: TelephoneTile, index: number) => {
    const isDouble = tile.left === tile.right;
    const telephone = board.telephones.find(t => t.tileIndex === index);
    const isActiveTelephone = activeTelephone?.tileIndex === index;

    if (!isDouble || !telephone?.isClosed) {
      return (
        <div key={index} className="flex items-center self-center">
          <TelephoneTileView
            tile={{ left: tile.left, right: tile.right }}
            horizontal={!isDouble}
            size="small"
            glowColor={tile.playedByColor}
            flipped={tile.isFlipped}
          />
        </div>
      );
    }

    const topTiles = board.tiles.filter(t => t.attachedToDoubleIndex === index && t.attachedSide === 'top');
    const bottomTiles = board.tiles.filter(t => t.attachedToDoubleIndex === index && t.attachedSide === 'bottom');

    const topTarget = telephone.hasTop ? telephone.topEnd : telephone.value;
    const bottomTarget = telephone.hasBottom ? telephone.bottomEnd : telephone.value;

    const maxExtensions = Math.max(topTiles.length, bottomTiles.length, 1);

    return (
      <div key={index} className="flex flex-col items-center self-center">
        <div className="flex flex-col items-center gap-0.5" style={{ minHeight: `${maxExtensions * 52}px` }}>
          <div className="flex-1" />
          {topTiles.length === 0 && isActiveTelephone && (
            <div className="w-6 h-6 border-2 border-dashed border-green-500/50 rounded flex items-center justify-center text-green-500/50 text-[10px]">
              {topTarget}↑
            </div>
          )}
          {[...topTiles].reverse().map((t, idx) => {
            const tileIsDouble = t.left === t.right;
            return (
              <TelephoneTileView
                key={`top-${idx}`}
                tile={{ left: t.left, right: t.right }}
                horizontal={tileIsDouble}
                size="small"
                glowColor={t.playedByColor}
                flipped={t.isFlipped}
              />
            );
          })}
        </div>

        <div className="relative">
          <TelephoneTileView
            tile={{ left: tile.left, right: tile.right }}
            horizontal={false}
            size="small"
            glowColor={tile.playedByColor}
          />
          {isActiveTelephone && (
            <div className="absolute -top-1 -right-1 bg-purple-600 text-white text-[8px] w-3 h-3 rounded-full flex items-center justify-center font-bold">T</div>
          )}
        </div>

        <div className="flex flex-col items-center gap-0.5" style={{ minHeight: `${maxExtensions * 52}px` }}>
          {bottomTiles.map((t, idx) => {
            const tileIsDouble = t.left === t.right;
            return (
              <TelephoneTileView
                key={`bottom-${idx}`}
                tile={{ left: t.left, right: t.right }}
                horizontal={tileIsDouble}
                size="small"
                glowColor={t.playedByColor}
                flipped={t.isFlipped}
              />
            );
          })}
          {bottomTiles.length === 0 && isActiveTelephone && (
            <div className="w-6 h-6 border-2 border-dashed border-green-500/50 rounded flex items-center justify-center text-green-500/50 text-[10px]">
              {bottomTarget}↓
            </div>
          )}
          <div className="flex-1" />
        </div>
      </div>
    );
  };

  return (
    <div className="bg-green-900/80 rounded-xl relative overflow-auto max-h-[600px]">
      <div className="absolute top-2 right-2 bg-amber-600 text-white text-sm px-3 py-1 rounded font-bold">
        Board: {boardPoints} {boardPoints % 5 === 0 && boardPoints > 0 && `(+${boardPoints})`}
      </div>

      <div className="flex items-center justify-center gap-1 py-4">
        <div className="text-amber-400 text-lg mr-2">←</div>

        {leftTiles.map((t) => {
          const tileIndex = board.tiles.indexOf(t);
          return renderTileWithExtensions(t, tileIndex);
        }).reverse()}

        {centerTile && renderTileWithExtensions(centerTile, board.tiles.indexOf(centerTile))}

        {rightTiles.map((t) => {
          const tileIndex = board.tiles.indexOf(t);
          return renderTileWithExtensions(t, tileIndex);
        })}

        <div className="text-amber-400 text-lg ml-2">→</div>
      </div>

      <div className="flex justify-center gap-4 mt-2 text-xs pb-2">
        <span className="px-2 py-1 bg-slate-700 rounded text-amber-400">
          ← {board.leftEnd ?? '?'}
        </span>
        {activeTelephone && (
          <span className="px-2 py-1 bg-purple-700 rounded text-purple-200">
            ↑{activeTelephone.hasTop ? activeTelephone.topEnd : activeTelephone.value}
            | Tel:{activeTelephone.value} |
            {activeTelephone.hasBottom ? activeTelephone.bottomEnd : activeTelephone.value}↓
          </span>
        )}
        <span className="px-2 py-1 bg-slate-700 rounded text-amber-400">
          {board.rightEnd ?? '?'} →
        </span>
      </div>
    </div>
  );
}

function PlayerSeat({
  participant,
  position,
  isMe,
}: {
  participant?: TelephoneGame['participants'][0];
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

  const isCurrentTurn = participant.isCurrentTurn;

  return (
    <div className={`
      flex ${isVertical ? 'flex-col' : 'flex-row'} items-center gap-3 p-4 rounded-xl
      ${isCurrentTurn ? 'bg-amber-900/50 ring-2 ring-amber-500' : 'bg-slate-800/70'}
    `}>
      <div
        className="w-12 h-12 rounded-full flex items-center justify-center text-white font-bold text-lg border-2 shrink-0"
        style={{ backgroundColor: participant.color, borderColor: isCurrentTurn ? '#fbbf24' : participant.color }}
      >
        {participant.displayName.charAt(0).toUpperCase()}
      </div>

      <div className="flex flex-col items-center text-center">
        <span className={`text-sm font-medium ${isMe ? 'text-amber-400' : 'text-white'}`}>
          {participant.displayName}
          {isMe && ' (You)'}
        </span>
        <div className="flex items-center gap-2 text-xs text-slate-400 mt-1">
          <span className="font-bold text-lg text-white">{participant.totalScore}</span>
          <span>pts</span>
          <span className="text-slate-500">|</span>
          <span>{participant.tileCount} tiles</span>
        </div>
      </div>
    </div>
  );
}

export default function TelephoneGameComponent({
  token,
  user,
  initialGame,
  onBack,
}: {
  token: string;
  user: User;
  initialGame: TelephoneGame;
  onBack: () => void;
}) {
  const [game, setGame] = useState<TelephoneGame>(initialGame);
  const [selectedTile, setSelectedTile] = useState<{ left: number; right: number } | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [newlyDrawnTiles, setNewlyDrawnTiles] = useState<{ left: number; right: number }[]>([]);
  const [countdown, setCountdown] = useState<number | null>(null);
  const { playSound, isMuted, toggleMute } = useSound();
  const [muted, setMuted] = useState(isMuted());

  const prevIsMyTurn = useRef(false);
  const prevStatus = useRef(game.status);
  const prevBoardLength = useRef(game.board.tiles.length);
  const prevBoneyardCount = useRef(game.boneyardCount);

  const handleToggleMute = () => {
    const newMuted = toggleMute();
    setMuted(newMuted);
  };

  const myParticipant = useMemo(() => {
    return game.participants.find((p) => p.userId === user.id);
  }, [game.participants, user.id]);

  const myPosition = myParticipant?.position ?? null;
  const isMyTurn = myParticipant?.isCurrentTurn ?? false;

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
    if (game.status === 'round_end' && countdown === null) {
      setCountdown(5);
    }
    if (game.status !== 'round_end') {
      setCountdown(null);
    }
  }, [game.status, countdown]);

  useEffect(() => {
    if (countdown === null || countdown <= 0) return;
    const timer = setTimeout(() => setCountdown(c => (c ?? 1) - 1), 1000);
    return () => clearTimeout(timer);
  }, [countdown]);

  useEffect(() => {
    if (countdown === 0 && game.status === 'round_end') {
      startNextRound();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [countdown, game.status]);

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
      playSound(dominoPlayer ? 'domino' : 'round_end');
    }
    prevStatus.current = game.status;
  }, [game.status, game.allHands, playSound]);

  useEffect(() => {
    if (game.board.tiles.length > prevBoardLength.current && game.board.tiles.length > 0) {
      const lastTile = game.board.tiles[game.board.tiles.length - 1];
      const isDouble = lastTile.left === lastTile.right;
      if (isDouble) {
        playSound('double_play', { file: `/sounds/double_${lastTile.left}.mp3`, volume: 0.5 });
      } else {
        playSound('tile_play');
      }
    }
    prevBoardLength.current = game.board.tiles.length;
  }, [game.board.tiles, playSound]);

  useEffect(() => {
    const boneyardDecreased = game.boneyardCount < prevBoneyardCount.current;
    const boardSame = game.board.tiles.length === prevBoardLength.current;
    if (boneyardDecreased && boardSame && game.status === 'active') {
      playSound('tile_draw');
    }
    prevBoneyardCount.current = game.boneyardCount;
  }, [game.boneyardCount, game.board.tiles.length, game.status, playSound]);

  const getRelativePosition = useCallback((targetPosition: number): 'bottom' | 'left' | 'top' | 'right' => {
    if (myPosition === null) return 'bottom';
    const playerCount = game.participants.length;
    const diff = (targetPosition - myPosition + playerCount) % playerCount;

    if (playerCount === 2) return diff === 0 ? 'bottom' : 'top';
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
    return positions.map(pos => ({
      position: pos,
      participant: game.participants.find(p => getRelativePosition(p.position) === pos)
    }));
  }, [game.participants, getRelativePosition]);

  const myHand = useMemo(() => {
    return game.myHand.map(t => ({ left: t[0], right: t[1] }));
  }, [game.myHand]);

  const canPlayTile = useCallback((tile: { left: number; right: number }): { sides: string[], disabledSides: string[] } => {
    if (!isMyTurn || game.status !== 'active') return { sides: [], disabledSides: [] };

    const isFirstTile = game.board.tiles.length === 0;

    if (isFirstTile && game.roundNumber === 1) {
      if (game.requiredFirstTile) {
        const matches = (tile.left === game.requiredFirstTile[0] && tile.right === game.requiredFirstTile[1]) ||
          (tile.left === game.requiredFirstTile[1] && tile.right === game.requiredFirstTile[0]);
        return { sides: matches ? ['center'] : [], disabledSides: [] };
      }
      return { sides: ['center'], disabledSides: [] };
    }

    if (isFirstTile) return { sides: ['center'], disabledSides: [] };

    const sides: string[] = [];
    const disabledSides: string[] = [];
    const isDouble = tile.left === tile.right;

    if (tile.left === game.board.leftEnd || tile.right === game.board.leftEnd) {
      sides.push('left');
    }
    if (tile.left === game.board.rightEnd || tile.right === game.board.rightEnd) {
      sides.push('right');
    }

    const activeTelephone = game.board.telephones.find(t => t.isClosed);

    if (activeTelephone) {
      const topTarget = activeTelephone.hasTop ? (activeTelephone.topEnd ?? activeTelephone.value) : activeTelephone.value;
      const bottomTarget = activeTelephone.hasBottom ? (activeTelephone.bottomEnd ?? activeTelephone.value) : activeTelephone.value;

      const tileMatchesTop = tile.left === topTarget || tile.right === topTarget;
      const tileMatchesBottom = tile.left === bottomTarget || tile.right === bottomTarget;

      const canPlayTopAsFirst = !isDouble && !activeTelephone.hasTop && tileMatchesTop;
      const canPlayTopAsChain = activeTelephone.hasTop && tileMatchesTop;
      const canPlayBottomAsFirst = !isDouble && !activeTelephone.hasBottom && tileMatchesBottom;
      const canPlayBottomAsChain = activeTelephone.hasBottom && tileMatchesBottom;

      if (canPlayTopAsFirst || canPlayTopAsChain) {
        sides.push(`top-${activeTelephone.tileIndex}`);
      } else {
        disabledSides.push(`top-${activeTelephone.tileIndex}`);
      }

      if (canPlayBottomAsFirst || canPlayBottomAsChain) {
        sides.push(`bottom-${activeTelephone.tileIndex}`);
      } else {
        disabledSides.push(`bottom-${activeTelephone.tileIndex}`);
      }
    }

    return { sides, disabledSides };
  }, [isMyTurn, game.status, game.board, game.roundNumber, game.requiredFirstTile]);

  const hasPlayableTile = useMemo(() => {
    return myHand.some(t => canPlayTile(t).sides.length > 0);
  }, [myHand, canPlayTile]);

  const isComboTile = useCallback((tile: { left: number; right: number }) => {
    if (!game.comboAvailable || !game.comboTiles) return false;
    return game.comboTiles.some(combo =>
      combo.some(t => (t[0] === tile.left && t[1] === tile.right) || (t[0] === tile.right && t[1] === tile.left))
    );
  }, [game.comboAvailable, game.comboTiles]);

  const playTile = async (side: string) => {
    if (!selectedTile || !isMyTurn) return;

    setLoading(true);
    setError(null);

    let actualSide = side;
    if (side.startsWith('top-') || side.startsWith('bottom-')) {
      actualSide = side.split('-')[0];
    }

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
          side: actualSide === 'center' ? 'right' : actualSide,
        }),
      });

      if (res.ok) {
        setSelectedTile(null);
        setNewlyDrawnTiles([]);
        await refreshGame();
      } else {
        const data = await res.json();
        setError(data.message || 'Failed to play tile');
      }
    } catch {
      setError('Network error');
    } finally {
      setLoading(false);
    }
  };

  const playCombo = async () => {
    if (!game.comboTiles || game.comboTiles.length === 0) return;

    setLoading(true);
    setError(null);

    const combo = game.comboTiles[0];

    try {
      const res = await fetch(`${API_BASE}/games/${game.id}/play-combo`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({
          tile1Left: combo[0][0],
          tile1Right: combo[0][1],
          tile2Left: combo[1][0],
          tile2Right: combo[1][1],
        }),
      });

      if (res.ok) {
        setSelectedTile(null);
        setNewlyDrawnTiles([]);
        await refreshGame();
      } else {
        const data = await res.json();
        setError(data.message || 'Failed to play combo');
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
      } else {
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

  const topPlayer = sortedParticipants.find(s => s.position === 'top');
  const leftPlayer = sortedParticipants.find(s => s.position === 'left');
  const rightPlayer = sortedParticipants.find(s => s.position === 'right');

  const playableInfo = selectedTile ? canPlayTile(selectedTile) : { sides: [], disabledSides: [] };

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
          >
            {muted ? <VolumeX size={18} /> : <Volume2 size={18} />}
          </button>

          {(game.status === 'waiting' || game.status === 'voting') && (
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
              <span className="px-2 py-0.5 bg-purple-600 text-white text-xs rounded">TELEPHONE</span>
            </div>

            <div className="flex items-center gap-2">
              <Layers size={16} className="text-slate-400" />
              <span className="text-white">Round {game.roundNumber}</span>
            </div>

            <div className="flex items-center gap-2 text-sm text-slate-300">
              <span className="text-slate-500">Boneyard:</span>
              <span className="font-medium">{game.boneyardCount}</span>
            </div>

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
              <div className="flex flex-col gap-0.5">
                {[...game.participants]
                  .sort((a, b) => b.totalScore - a.totalScore)
                  .map((p) => (
                    <div key={p.userId} className="flex items-center justify-between gap-3">
                      <span className="text-xs truncate max-w-20" style={{ color: p.color }}>
                        {p.displayName}
                      </span>
                      <span className="text-white font-medium text-xs">{p.totalScore}/365</span>
                    </div>
                  ))}
              </div>
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
            <TelephoneBoardView board={game.board} boardPoints={game.boardPoints} />
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
                Votes: {game.participants.filter(p => p.hasVotedToStart).length}/{game.participants.length}
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
              <p className="text-white text-lg font-semibold mb-2">Round {game.roundNumber} Complete</p>
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
                              ph.hand.map((t, idx) => (
                                <TelephoneTileView key={idx} tile={{ left: t[0], right: t[1] }} size="tiny" />
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
                      Waiting for {game.participants.find(p => p.isCurrentTurn)?.displayName}...
                    </span>
                  )}
                  {game.comboAvailable && isMyTurn && (
                    <span className="px-2 py-1 bg-green-500 text-white text-xs rounded animate-bounce">
                      COMBO AVAILABLE!
                    </span>
                  )}
                </div>

                <div className="flex items-center gap-2">
                  {game.comboAvailable && isMyTurn && (
                    <button
                      onClick={playCombo}
                      disabled={loading}
                      className="px-4 py-2 bg-green-600 hover:bg-green-500 text-white rounded-lg text-sm font-semibold transition animate-pulse"
                    >
                      Play Combo!
                    </button>
                  )}

                  {isMyTurn && !hasPlayableTile && game.playableBoneyardCount > 0 && (
                    <button
                      onClick={drawOrPass}
                      disabled={loading}
                      className="px-4 py-2 bg-amber-600 hover:bg-amber-500 text-white rounded-lg flex items-center gap-2 text-sm transition animate-pulse"
                    >
                      <RotateCcw size={16} />
                      Draw {game.participants.length === 2 ? 2 : 1}
                    </button>
                  )}
                </div>
              </div>

              <div className="flex gap-2 flex-wrap justify-center">
                {myHand.map((tile, idx) => {
                  const { sides } = canPlayTile(tile);
                  const playable = sides.length > 0;
                  const isSelected = selectedTile && selectedTile.left === tile.left && selectedTile.right === tile.right;
                  const isNewlyDrawn = newlyDrawnTiles.some(t => t.left === tile.left && t.right === tile.right);
                  const isCombo = isComboTile(tile);

                  return (
                    <div key={idx} className="relative">
                      {isNewlyDrawn && (
                        <div className="absolute -top-2 left-1/2 -translate-x-1/2 bg-cyan-500 text-white text-[10px] px-1.5 rounded-full z-10">
                          NEW
                        </div>
                      )}
                      <TelephoneTileView
                        tile={tile}
                        selected={!!isSelected}
                        playable={playable}
                        comboHighlight={isCombo}
                        onClick={() => {
                          if (isMyTurn && playable) {
                            if (isSelected) {
                              setSelectedTile(null);
                            } else {
                              setSelectedTile(tile);
                            }
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

              {selectedTile && playableInfo.sides.length > 0 && (
                <div className="mt-4 flex gap-3 justify-center flex-wrap">
                  {playableInfo.sides.includes('left') && (
                    <button
                      onClick={() => playTile('left')}
                      disabled={loading}
                      className="px-6 py-2.5 bg-green-600 hover:bg-green-500 text-white rounded-lg font-semibold transition"
                    >
                      ← Play Left
                    </button>
                  )}
                  {playableInfo.sides.includes('right') && (
                    <button
                      onClick={() => playTile('right')}
                      disabled={loading}
                      className="px-6 py-2.5 bg-green-600 hover:bg-green-500 text-white rounded-lg font-semibold transition"
                    >
                      Play Right →
                    </button>
                  )}
                  {playableInfo.sides.includes('center') && (
                    <button
                      onClick={() => playTile('center')}
                      disabled={loading}
                      className="px-6 py-2.5 bg-green-600 hover:bg-green-500 text-white rounded-lg font-semibold transition"
                    >
                      Play Center
                    </button>
                  )}
                  {playableInfo.sides.filter(s => s.startsWith('top-')).map(s => (
                    <button
                      key={s}
                      onClick={() => playTile('top')}
                      disabled={loading}
                      className="px-6 py-2.5 bg-purple-600 hover:bg-purple-500 text-white rounded-lg font-semibold transition"
                    >
                      ↑ Play Top (Tel)
                    </button>
                  ))}
                  {playableInfo.disabledSides?.filter(s => s.startsWith('top-')).map(s => (
                    <button
                      key={s}
                      disabled
                      className="px-6 py-2.5 bg-slate-700 text-slate-500 rounded-lg font-semibold cursor-not-allowed"
                    >
                      ↑ Top (Tel)
                    </button>
                  ))}
                  {playableInfo.sides.filter(s => s.startsWith('bottom-')).map(s => (
                    <button
                      key={s}
                      onClick={() => playTile('bottom')}
                      disabled={loading}
                      className="px-6 py-2.5 bg-purple-600 hover:bg-purple-500 text-white rounded-lg font-semibold transition"
                    >
                      ↓ Play Bottom (Tel)
                    </button>
                  ))}
                  {playableInfo.disabledSides?.filter(s => s.startsWith('bottom-')).map(s => (
                    <button
                      key={s}
                      disabled
                      className="px-6 py-2.5 bg-slate-700 text-slate-500 rounded-lg font-semibold cursor-not-allowed"
                    >
                      ↓ Bottom (Tel)
                    </button>
                  ))}
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
                  <p className="text-amber-400 text-sm mb-2">No playable tiles and boneyard exhausted.</p>
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