export interface Tile {
  left: number;
  right: number;
}

export interface TileWithMeta extends Tile {
  playedById?: string;
  playedByPosition?: number;
  playedByColor?: string;
  side?: 'left' | 'right';
  isFlipped?: boolean;
}

export interface Player {
  id: string;
  displayName: string;
  position: number;
  team: number;
  color: string;
  hand: Tile[];
  totalScore: number;
  roundScore: number;
  consecutiveLowWins: number;
  hasVotedToStart: boolean;
}

export interface RoundHistoryEntry {
  roundNumber: number;
  winnerId: string | null;
  winningTeam: number | null;
  pointsWon: number;
  totalAwarded?: number;
  hadDeferred?: boolean;
  reason: string;
  tiedSum?: number;
  deferredTotal?: number;
}

export interface GameState {
  id: string;
  code: string;
  board: TileWithMeta[];
  boardLeft: number | null;
  boardRight: number | null;
  players: Player[];
  currentTurn: number;
  boneyard: Tile[];
  boneyardCount: number;
  status: 'waiting' | 'voting' | 'active' | 'round_end' | 'finished';
  roundNumber: number;
  roundStarterId: string | null;
  roundHistory: RoundHistoryEntry[];
  isTeamGame: boolean;
  team1Score: number;
  team2Score: number;
  deferredPoints: number;
  outcome?: string;
  reason?: string;
  winnerId?: string;
  winningTeam?: number;
}

const PLAYER_COLORS = ['#e63946', '#2a9d8f', '#e9c46a', '#9b5de5'];
const WIN_SCORE = 101;
const LOW_WIN_THRESHOLD = 13;
const CONSECUTIVE_RESET_COUNT = 3;
const TILES_PER_PLAYER = 7;

export function generateFullSet(): Tile[] {
  const tiles: Tile[] = [];
  for (let i = 0; i <= 6; i++) {
    for (let j = i; j <= 6; j++) {
      tiles.push({ left: i, right: j });
    }
  }
  return tiles;
}

export function shuffleTiles(tiles: Tile[]): Tile[] {
  const arr = [...tiles];
  for (let i = arr.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [arr[i], arr[j]] = [arr[j], arr[i]];
  }
  return arr;
}

export function calculateHandScore(hand: Tile[]): number {
  return hand.reduce((sum, t) => {
    if (t.left === 0 && t.right === 0) return sum + 10;
    return sum + t.left + t.right;
  }, 0);
}

export function getUntouchableCount(playerCount: number): number {
  if (playerCount === 2) return 2;
  if (playerCount === 3) return 1;
  return 0;
}

export function getDrawCount(playerCount: number): number {
  if (playerCount === 2) return 2;
  if (playerCount === 3) return 1;
  return 0;
}

export function getPlayableBoneyardCount(boneyardSize: number, playerCount: number): number {
  return Math.max(0, boneyardSize - getUntouchableCount(playerCount));
}

export function canPlay(tile: Tile, boardLeft: number | null, boardRight: number | null): boolean {
  if (boardLeft === null || boardRight === null) return true;
  return tile.left === boardLeft || tile.right === boardLeft ||
    tile.left === boardRight || tile.right === boardRight;
}

export function canPlayerPlay(hand: Tile[], boardLeft: number | null, boardRight: number | null): boolean {
  return hand.some(t => canPlay(t, boardLeft, boardRight));
}

export function getPlayableTiles(hand: Tile[], boardLeft: number | null, boardRight: number | null): Tile[] {
  return hand.filter(t => canPlay(t, boardLeft, boardRight));
}

export function canPlayOnSide(
  tile: Tile,
  side: 'left' | 'right',
  boardLeft: number | null,
  boardRight: number | null
): boolean {
  if (boardLeft === null || boardRight === null) return true;
  const target = side === 'left' ? boardLeft : boardRight;
  return tile.left === target || tile.right === target;
}

export function tilesEqual(a: Tile, b: Tile): boolean {
  return (a.left === b.left && a.right === b.right) ||
    (a.left === b.right && a.right === b.left);
}

export function removeTileFromHand(hand: Tile[], tile: Tile): Tile[] {
  const idx = hand.findIndex(t => tilesEqual(t, tile));
  if (idx === -1) return hand;
  return [...hand.slice(0, idx), ...hand.slice(idx + 1)];
}

export function hasOneOne(hand: Tile[]): boolean {
  return hand.some(t => t.left === 1 && t.right === 1);
}

export function isDouble(tile: Tile): boolean {
  return tile.left === tile.right;
}

export function findFirstRoundStarterTile(players: Player[]): { tile: Tile; position: number } | null {
  const priorityTiles: Tile[] = [
    { left: 1, right: 1 },
    { left: 2, right: 2 },
    { left: 3, right: 3 },
    { left: 4, right: 4 },
    { left: 5, right: 5 },
    { left: 6, right: 6 },
  ];

  for (const tile of priorityTiles) {
    for (const p of [...players].sort((a, b) => a.position - b.position)) {
      if (p.hand.some(t => t.left === tile.left && t.right === tile.right)) {
        return { tile, position: p.position };
      }
    }
  }

  return null;
}

export function assignColor(position: number): string {
  return PLAYER_COLORS[position % PLAYER_COLORS.length];
}

export function assignTeam(position: number, isTeamGame: boolean): number {
  if (!isTeamGame) return 0;
  return (position % 2) + 1;
}

export interface PlayValidation {
  valid: boolean;
  error?: string;
  orientedLeft: number;
  orientedRight: number;
  newBoardEnd: number;
  isFlipped: boolean;
}

export function validatePlay(
  boardLeft: number | null,
  boardRight: number | null,
  tileLeft: number,
  tileRight: number,
  side: 'left' | 'right',
  isFirstTile: boolean,
  isFirstRound: boolean
): PlayValidation {
  const invalid = (error: string): PlayValidation => ({
    valid: false,
    error,
    orientedLeft: 0,
    orientedRight: 0,
    newBoardEnd: 0,
    isFlipped: false
  });

  if (isFirstTile && isFirstRound) {
    if (tileLeft !== tileRight || tileLeft < 1 || tileLeft > 6) {
      return invalid('First round must start with a double');
    }
    return { valid: true, orientedLeft: tileLeft, orientedRight: tileRight, newBoardEnd: tileLeft, isFlipped: false };
  }

  if (isFirstTile) {
    return {
      valid: true,
      orientedLeft: tileLeft,
      orientedRight: tileRight,
      newBoardEnd: side === 'left' ? tileLeft : tileRight,
      isFlipped: false
    };
  }

  const target = side === 'left' ? boardLeft! : boardRight!;

  if (tileLeft === target) {
    const newEnd = tileRight;
    return {
      valid: true,
      orientedLeft: side === 'left' ? tileRight : tileLeft,
      orientedRight: side === 'left' ? tileLeft : tileRight,
      newBoardEnd: newEnd,
      isFlipped: side === 'left'
    };
  }

  if (tileRight === target) {
    const newEnd = tileLeft;
    return {
      valid: true,
      orientedLeft: side === 'left' ? tileLeft : tileRight,
      orientedRight: side === 'left' ? tileRight : tileLeft,
      newBoardEnd: newEnd,
      isFlipped: side !== 'left'
    };
  }

  return invalid(`Tile [${tileLeft}|${tileRight}] cannot be played on ${side} (needs ${target})`);
}

export interface RoundResult {
  winnerId: string | null;
  winningTeam: number | null;
  pointsWon: number;
  reason: 'domino' | 'block' | 'tie';
  nextRoundStarterId: string | null;
  isTie: boolean;
  tiedSum: number;
}

export function determineRoundWinner(
  players: Player[],
  isTeamGame: boolean,
  blockerId?: string
): RoundResult {
  const dominoWinner = players.find(p => p.hand.length === 0);

  if (dominoWinner) {
    let pointsWon: number;
    if (isTeamGame) {
      pointsWon = players
        .filter(p => p.team !== dominoWinner.team)
        .reduce((sum, p) => sum + calculateHandScore(p.hand), 0);
    } else {
      pointsWon = players
        .filter(p => p.id !== dominoWinner.id)
        .reduce((sum, p) => sum + calculateHandScore(p.hand), 0);
    }

    return {
      winnerId: dominoWinner.id,
      winningTeam: isTeamGame ? dominoWinner.team : null,
      pointsWon,
      reason: 'domino',
      nextRoundStarterId: dominoWinner.id,
      isTie: false,
      tiedSum: 0
    };
  }

  if (isTeamGame) {
    const team1Sum = players.filter(p => p.team === 1).reduce((s, p) => s + calculateHandScore(p.hand), 0);
    const team2Sum = players.filter(p => p.team === 2).reduce((s, p) => s + calculateHandScore(p.hand), 0);

    if (team1Sum === team2Sum) {
      const starter = blockerId ?? players.sort((a, b) => a.position - b.position)[0].id;
      return {
        winnerId: null,
        winningTeam: null,
        pointsWon: 0,
        reason: 'tie',
        nextRoundStarterId: starter,
        isTie: true,
        tiedSum: team1Sum
      };
    }

    const winningTeam = team1Sum < team2Sum ? 1 : 2;
    const pointsWon = Math.abs(team2Sum - team1Sum);
    const teamPlayers = players.filter(p => p.team === winningTeam).sort((a, b) => a.position - b.position);
    const starter = (blockerId && teamPlayers.some(p => p.id === blockerId))
      ? blockerId
      : teamPlayers[0].id;

    return {
      winnerId: starter,
      winningTeam,
      pointsWon,
      reason: 'block',
      nextRoundStarterId: starter,
      isTie: false,
      tiedSum: 0
    };
  } else {
    const scores = players.map(p => ({ id: p.id, score: calculateHandScore(p.hand) }));
    const minScore = Math.min(...scores.map(s => s.score));
    const winners = scores.filter(s => s.score === minScore);

    if (winners.length > 1) {
      const starter = blockerId ?? players.sort((a, b) => a.position - b.position)[0].id;
      return {
        winnerId: null,
        winningTeam: null,
        pointsWon: 0,
        reason: 'tie',
        nextRoundStarterId: starter,
        isTie: true,
        tiedSum: minScore
      };
    }

    const totalOthers = scores.filter(s => s.id !== winners[0].id).reduce((sum, s) => sum + s.score, 0);
    const pointsWon = totalOthers - minScore;

    return {
      winnerId: winners[0].id,
      winningTeam: null,
      pointsWon,
      reason: 'block',
      nextRoundStarterId: winners[0].id,
      isTie: false,
      tiedSum: 0
    };
  }
}

export function applyRoundResult(
  state: GameState,
  result: RoundResult
): GameState {
  const newState = { ...state };

  if (result.isTie) {
    newState.deferredPoints += result.tiedSum;
    newState.roundStarterId = result.nextRoundStarterId;
    newState.status = 'round_end';
    newState.roundHistory = [
      ...state.roundHistory,
      {
        roundNumber: state.roundNumber,
        winnerId: null,
        winningTeam: null,
        pointsWon: 0,
        reason: 'tie',
        tiedSum: result.tiedSum,
        deferredTotal: newState.deferredPoints
      }
    ];
    return newState;
  }

  let totalPoints = result.pointsWon;
  const hadDeferred = newState.deferredPoints > 0;

  if (hadDeferred) {
    if (result.pointsWon > 0) {
      totalPoints = (newState.deferredPoints * 2) + result.pointsWon;
    }
    newState.deferredPoints = 0;
  }

  const updatedPlayers = state.players.map(p => ({ ...p }));
  const isLowWin = result.pointsWon < LOW_WIN_THRESHOLD && result.pointsWon > 0;

  if (state.isTeamGame && result.winningTeam !== null) {
    if (result.winningTeam === 1) {
      newState.team1Score = state.team1Score + totalPoints;
    } else {
      newState.team2Score = state.team2Score + totalPoints;
    }

    const losingTeam = result.winningTeam === 1 ? 2 : 1;

    for (const p of updatedPlayers) {
      if (p.team === losingTeam) {
        p.consecutiveLowWins = isLowWin ? p.consecutiveLowWins + 1 : 0;
        if (p.consecutiveLowWins >= CONSECUTIVE_RESET_COUNT) {
          if (losingTeam === 1) newState.team1Score = 0;
          else newState.team2Score = 0;
          p.consecutiveLowWins = 0;
        }
      } else {
        p.consecutiveLowWins = 0;
      }
    }
  } else if (result.winnerId) {
    for (const p of updatedPlayers) {
      if (p.id === result.winnerId) {
        p.totalScore += totalPoints;
        p.roundScore = totalPoints;
        p.consecutiveLowWins = 0;
      } else {
        p.roundScore = 0;
        p.consecutiveLowWins = isLowWin ? p.consecutiveLowWins + 1 : 0;
        if (p.consecutiveLowWins >= CONSECUTIVE_RESET_COUNT) {
          p.totalScore = 0;
          p.consecutiveLowWins = 0;
        }
      }
    }
  }

  newState.players = updatedPlayers;
  newState.roundStarterId = result.nextRoundStarterId;

  newState.roundHistory = [
    ...state.roundHistory,
    {
      roundNumber: state.roundNumber,
      winnerId: result.winnerId,
      winningTeam: result.winningTeam,
      pointsWon: result.pointsWon,
      totalAwarded: totalPoints,
      hadDeferred,
      reason: result.reason
    }
  ];

  const matchOver = checkMatchWin(newState);

  if (matchOver) {
    newState.status = 'finished';
    if (state.isTeamGame) {
      newState.winningTeam = newState.team1Score >= WIN_SCORE ? 1 : 2;
      newState.winnerId = updatedPlayers.find(p => p.team === newState.winningTeam)?.id;
      newState.outcome = 'win';
      newState.reason = `Team ${newState.winningTeam} wins with ${newState.winningTeam === 1 ? newState.team1Score : newState.team2Score} points`;
    } else {
      const winner = updatedPlayers.find(p => p.totalScore >= WIN_SCORE);
      if (winner) {
        newState.winnerId = winner.id;
        newState.outcome = 'win';
        newState.reason = `${winner.displayName} wins with ${winner.totalScore} points`;
      }
    }
  } else {
    newState.status = 'round_end';
  }

  return newState;
}

export function checkMatchWin(state: GameState): boolean {
  if (state.isTeamGame) {
    return state.team1Score >= WIN_SCORE || state.team2Score >= WIN_SCORE;
  }
  return state.players.some(p => p.totalScore >= WIN_SCORE);
}

export function isGameBlocked(
  players: Player[],
  boardLeft: number | null,
  boardRight: number | null,
  boneyard: Tile[],
  boneyardCount: number
): boolean {
  if (boardLeft === null || boardRight === null) return false;

  const playerCount = players.length;
  const playableCount = getPlayableBoneyardCount(boneyardCount, playerCount);

  if (playableCount > 0) {
    const playableBoneyard = boneyard.slice(0, playableCount);
    if (playableBoneyard.some(t => canPlay(t, boardLeft, boardRight))) {
      return false;
    }
  }

  return !players.some(p => canPlayerPlay(p.hand, boardLeft, boardRight));
}

export function createInitialState(
  id: string,
  code: string,
  maxPlayers: number,
  isTeamGame: boolean
): GameState {
  return {
    id,
    code,
    board: [],
    boardLeft: null,
    boardRight: null,
    players: [],
    currentTurn: 0,
    boneyard: [],
    boneyardCount: 0,
    status: 'waiting',
    roundNumber: 0,
    roundStarterId: null,
    roundHistory: [],
    isTeamGame,
    team1Score: 0,
    team2Score: 0,
    deferredPoints: 0
  };
}

export function startRound(state: GameState): GameState {
  const isFirstRound = state.roundNumber === 0;
  const playerCount = state.players.length;
  let starterIndex = 0;

  let tiles = shuffleTiles(generateFullSet());
  let hands: Tile[][] = [];

  if (isFirstRound) {
    const maxAttempts = 10;
    let attempt = 0;

    while (attempt < maxAttempts) {
      hands = state.players.map((_, idx) =>
        tiles.slice(idx * TILES_PER_PLAYER, (idx + 1) * TILES_PER_PLAYER)
      );

      const tempBoneyard = tiles.slice(playerCount * TILES_PER_PLAYER);
      
      const tempPlayers = state.players.map((p, idx) => ({
        ...p,
        hand: hands[idx]
      }));

      const starterInfo = findFirstRoundStarterTile(tempPlayers);

      if (!starterInfo) {
        attempt++;
        tiles = shuffleTiles(generateFullSet());
        continue;
      }

      const requiredTile = starterInfo.tile;
      const requiredInBoneyard = tempBoneyard.some(
        t => t.left === requiredTile.left && t.right === requiredTile.right
      );

      if (!requiredInBoneyard) {
        starterIndex = starterInfo.position;
        break;
      }

      attempt++;
      tiles = shuffleTiles(generateFullSet());
    }

    if (attempt === maxAttempts) {
      hands = state.players.map((_, idx) =>
        tiles.slice(idx * TILES_PER_PLAYER, (idx + 1) * TILES_PER_PLAYER)
      );
      const tempPlayers = state.players.map((p, idx) => ({ ...p, hand: hands[idx] }));
      const starterInfo = findFirstRoundStarterTile(tempPlayers);
      if (starterInfo) {
        starterIndex = starterInfo.position;
      }
    }
  } else {
    hands = state.players.map((_, idx) =>
      tiles.slice(idx * TILES_PER_PLAYER, (idx + 1) * TILES_PER_PLAYER)
    );

    if (state.roundStarterId) {
      const starter = state.players.find(p => p.id === state.roundStarterId);
      if (starter) starterIndex = starter.position;
    }
  }

  const updatedPlayers = state.players.map((p, idx) => ({
    ...p,
    hand: hands[idx],
    roundScore: 0,
    hasVotedToStart: false
  }));

  const boneyard = tiles.slice(playerCount * TILES_PER_PLAYER);

  return {
    ...state,
    players: updatedPlayers,
    board: [],
    boardLeft: null,
    boardRight: null,
    boneyard,
    boneyardCount: boneyard.length,
    currentTurn: starterIndex,
    roundNumber: state.roundNumber + 1,
    status: 'active'
  };
}

export function playTile(
  state: GameState,
  playerId: string,
  tile: Tile,
  side: 'left' | 'right'
): { state: GameState; error?: string } {
  if (state.status !== 'active') {
    return { state, error: 'Round not active' };
  }

  const playerIdx = state.players.findIndex(p => p.id === playerId);
  if (playerIdx === -1) {
    return { state, error: 'Player not found' };
  }

  const player = state.players[playerIdx];
  if (state.currentTurn !== player.position) {
    return { state, error: 'Not your turn' };
  }

  const tileInHand = player.hand.find(t => tilesEqual(t, tile));
  if (!tileInHand) {
    return { state, error: 'Tile not in hand' };
  }

  const isFirstTile = state.board.length === 0;
  const isFirstRound = state.roundNumber === 1;

  const validation = validatePlay(
    state.boardLeft,
    state.boardRight,
    tile.left,
    tile.right,
    side,
    isFirstTile,
    isFirstRound
  );

  if (!validation.valid) {
    return { state, error: validation.error };
  }

  const newHand = removeTileFromHand(player.hand, tile);
  const updatedPlayers = state.players.map(p =>
    p.id === playerId ? { ...p, hand: newHand } : p
  );

  const boardTile: TileWithMeta = {
    left: validation.orientedLeft,
    right: validation.orientedRight,
    playedById: playerId,
    playedByPosition: player.position,
    playedByColor: player.color,
    side,
    isFlipped: validation.isFlipped
  };

  let newBoardLeft = state.boardLeft;
  let newBoardRight = state.boardRight;

  if (isFirstTile) {
    newBoardLeft = validation.orientedLeft;
    newBoardRight = validation.orientedRight;
  } else if (side === 'left') {
    newBoardLeft = validation.newBoardEnd;
  } else {
    newBoardRight = validation.newBoardEnd;
  }

  let newState: GameState = {
    ...state,
    players: updatedPlayers,
    board: [...state.board, boardTile],
    boardLeft: newBoardLeft,
    boardRight: newBoardRight,
    currentTurn: (state.currentTurn + 1) % state.players.length
  };

  if (newHand.length === 0) {
    const result = determineRoundWinner(newState.players, newState.isTeamGame);
    newState = applyRoundResult(newState, result);
  } else if (isGameBlocked(newState.players, newBoardLeft, newBoardRight, newState.boneyard, newState.boneyardCount)) {
    const result = determineRoundWinner(newState.players, newState.isTeamGame, playerId);
    newState = applyRoundResult(newState, result);
  }

  return { state: newState };
}

export function drawTiles(
  state: GameState,
  playerId: string
): { state: GameState; drawnTiles: Tile[]; error?: string } {
  if (state.status !== 'active') {
    return { state, drawnTiles: [], error: 'Round not active' };
  }

  const playerIdx = state.players.findIndex(p => p.id === playerId);
  if (playerIdx === -1) {
    return { state, drawnTiles: [], error: 'Player not found' };
  }

  const player = state.players[playerIdx];
  if (state.currentTurn !== player.position) {
    return { state, drawnTiles: [], error: 'Not your turn' };
  }

  if (canPlayerPlay(player.hand, state.boardLeft, state.boardRight)) {
    return { state, drawnTiles: [], error: 'You have a playable tile' };
  }

  const playerCount = state.players.length;
  const playableCount = getPlayableBoneyardCount(state.boneyardCount, playerCount);

  if (playableCount === 0) {
    return { state, drawnTiles: [], error: 'No tiles available' };
  }

  const drawCount = Math.min(getDrawCount(playerCount), playableCount);
  const drawnTiles = state.boneyard.slice(0, drawCount);
  const newBoneyard = state.boneyard.slice(drawCount);

  const newHand = [...player.hand, ...drawnTiles];
  const updatedPlayers = state.players.map(p =>
    p.id === playerId ? { ...p, hand: newHand } : p
  );

  let newState: GameState = {
    ...state,
    players: updatedPlayers,
    boneyard: newBoneyard,
    boneyardCount: newBoneyard.length
  };

  if (isGameBlocked(newState.players, newState.boardLeft, newState.boardRight, newState.boneyard, newState.boneyardCount)) {
    const result = determineRoundWinner(newState.players, newState.isTeamGame);
    newState = applyRoundResult(newState, result);
  }

  return { state: newState, drawnTiles };
}

export function voteToStart(state: GameState, playerId: string): GameState {
  const updatedPlayers = state.players.map(p =>
    p.id === playerId ? { ...p, hasVotedToStart: true } : p
  );

  const allVoted = updatedPlayers.every(p => p.hasVotedToStart);

  return {
    ...state,
    players: updatedPlayers,
    status: allVoted ? 'active' : 'voting'
  };
}

export function addPlayer(
  state: GameState,
  playerId: string,
  displayName: string,
  position?: number
): GameState {
  const takenPositions = new Set(state.players.map(p => p.position));

  let chosenPosition: number;
  if (position !== undefined && !takenPositions.has(position)) {
    chosenPosition = position;
  } else {
    chosenPosition = 0;
    while (takenPositions.has(chosenPosition)) chosenPosition++;
  }

  const newPlayer: Player = {
    id: playerId,
    displayName,
    position: chosenPosition,
    team: assignTeam(chosenPosition, state.isTeamGame),
    color: assignColor(chosenPosition),
    hand: [],
    totalScore: 0,
    roundScore: 0,
    consecutiveLowWins: 0,
    hasVotedToStart: false
  };

  return {
    ...state,
    players: [...state.players, newPlayer].sort((a, b) => a.position - b.position)
  };
}

export function removePlayer(state: GameState, playerId: string): GameState {
  return {
    ...state,
    players: state.players.filter(p => p.id !== playerId)
  };
}