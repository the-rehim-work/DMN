using backend.Data;
using System.Text.Json;

namespace backend.Services
{
    public interface ITelephoneGameService
    {
        List<int[]> GenerateFullSet();
        void ShuffleTiles(List<int[]> tiles);
        int CalculateHandScore(List<int[]> hand);
        bool CanPlay(int tileLeft, int tileRight, TelephoneBoardState board);
        bool CanPlayerPlay(List<int[]> hand, TelephoneBoardState board);
        bool IsGameBlocked(Game game, List<GameParticipant> participants, TelephoneBoardState board);
        int GetUntouchableCount(int playerCount);
        int GetDrawCount(int playerCount);
        int GetPlayableBoneyardCount(List<int[]> boneyard, int playerCount);
        TelephonePlayResult ValidateAndPlay(TelephoneBoardState board, int tileLeft, int tileRight, string side, bool isFirstTile);
        int CalculateBoardPoints(TelephoneBoardState board);
        TelephoneRoundResult DetermineRoundWinner(Game game, List<GameParticipant> participants, Guid? blockerId);
        bool CheckMatchWin(List<GameParticipant> participants);
        string AssignColor(int position);
        int FindFirstRoundStarter(List<GameParticipant> participants);
        int[]? GetRequiredFirstTile(List<GameParticipant> participants);
        bool ShouldReshuffleHand(List<int[]> hand);
        List<int[][]>? FindComboPlay(List<int[]> hand, TelephoneBoardState board);
    }

    public sealed class TelephoneBoardState
    {
        public int? LeftEnd { get; set; }
        public int? RightEnd { get; set; }
        public List<TelephoneTile> Tiles { get; set; } = new();
        public List<TelephoneDouble> Telephones { get; set; } = new();
    }

    public sealed class TelephoneTile
    {
        public int Left { get; set; }
        public int Right { get; set; }
        public string Position { get; set; } = "center";
        public int? AttachedToDoubleIndex { get; set; }
        public string? AttachedSide { get; set; }
        public Guid? PlayedById { get; set; }
        public int? PlayedByPosition { get; set; }
        public string? PlayedByColor { get; set; }
        public bool IsFlipped { get; set; }
    }

    public sealed class TelephoneDouble
    {
        public int Value { get; set; }
        public int TileIndex { get; set; }
        public bool IsClosed { get; set; }
        public int? TopEnd { get; set; }
        public int? BottomEnd { get; set; }
        public bool HasTop { get; set; }
        public bool HasBottom { get; set; }
    }

    public sealed class TelephoneGameService : ITelephoneGameService
    {
        private static readonly string[] PlayerColors = { "#e63946", "#2a9d8f", "#e9c46a", "#9b5de5" };
        private const int WinScore = 365;
        private const int PointCapThreshold = 300;

        public List<int[]> GenerateFullSet()
        {
            var tiles = new List<int[]>();
            for (int i = 0; i <= 6; i++)
                for (int j = i; j <= 6; j++)
                    tiles.Add(new[] { i, j });
            return tiles;
        }

        public void ShuffleTiles(List<int[]> tiles)
        {
            var rng = Random.Shared;
            for (int i = tiles.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (tiles[i], tiles[j]) = (tiles[j], tiles[i]);
            }
        }

        public int CalculateHandScore(List<int[]> hand)
        {
            if (hand.Count == 0) return 0;

            bool hasOnlyDoubleZero = hand.Count == 1 && hand[0][0] == 0 && hand[0][1] == 0;
            if (hasOnlyDoubleZero) return 10;

            return hand.Sum(t => t[0] + t[1]);
        }

        public bool ShouldReshuffleHand(List<int[]> hand)
        {
            var doubleCount = hand.Count(t => t[0] == t[1]);
            if (doubleCount >= 5) return true;

            for (int digit = 0; digit <= 6; digit++)
            {
                var tilesWithDigit = hand.Where(t => t[0] == digit || t[1] == digit).ToList();
                var hasDouble = tilesWithDigit.Any(t => t[0] == t[1] && t[0] == digit);
                if (tilesWithDigit.Count >= 5 && !hasDouble)
                    return true;
            }

            return false;
        }

        public int[]? GetRequiredFirstTile(List<GameParticipant> participants)
        {
            foreach (var p in participants.OrderBy(x => x.Position))
            {
                var hand = ParseTiles(p.HandJson);
                if (hand.Any(t => (t[0] == 2 && t[1] == 3) || (t[0] == 3 && t[1] == 2)))
                    return new[] { 2, 3 };
            }

            int[][] priorityDoubles = { new[] { 1, 1 }, new[] { 2, 2 }, new[] { 3, 3 }, new[] { 4, 4 }, new[] { 5, 5 }, new[] { 6, 6 } };

            foreach (var tile in priorityDoubles)
            {
                foreach (var p in participants.OrderBy(x => x.Position))
                {
                    var hand = ParseTiles(p.HandJson);
                    if (hand.Any(t => t[0] == tile[0] && t[1] == tile[1]))
                        return tile;
                }
            }

            return null;
        }

        public int FindFirstRoundStarter(List<GameParticipant> participants)
        {
            foreach (var p in participants.OrderBy(x => x.Position))
            {
                var hand = ParseTiles(p.HandJson);
                if (hand.Any(t => (t[0] == 2 && t[1] == 3) || (t[0] == 3 && t[1] == 2)))
                    return p.Position;
            }

            int[][] priorityDoubles = { new[] { 1, 1 }, new[] { 2, 2 }, new[] { 3, 3 }, new[] { 4, 4 }, new[] { 5, 5 }, new[] { 6, 6 } };

            foreach (var tile in priorityDoubles)
            {
                foreach (var p in participants.OrderBy(x => x.Position))
                {
                    var hand = ParseTiles(p.HandJson);
                    if (hand.Any(t => t[0] == tile[0] && t[1] == tile[1]))
                        return p.Position;
                }
            }

            return participants.OrderBy(x => x.Position).First().Position;
        }

        public bool CanPlay(int tileLeft, int tileRight, TelephoneBoardState board)
        {
            if (board.Tiles.Count == 0) return true;

            if (tileLeft == board.LeftEnd || tileRight == board.LeftEnd) return true;
            if (tileLeft == board.RightEnd || tileRight == board.RightEnd) return true;

            bool isDouble = tileLeft == tileRight;
            var activeTelephone = board.Telephones.FirstOrDefault(t => t.IsClosed);
            if (activeTelephone != null)
            {
                int topTarget = activeTelephone.HasTop ? (activeTelephone.TopEnd ?? activeTelephone.Value) : activeTelephone.Value;
                int bottomTarget = activeTelephone.HasBottom ? (activeTelephone.BottomEnd ?? activeTelephone.Value) : activeTelephone.Value;

                bool canPlayTop = (tileLeft == topTarget || tileRight == topTarget);
                bool canPlayBottom = (tileLeft == bottomTarget || tileRight == bottomTarget);

                if (isDouble)
                {
                    if (activeTelephone.HasTop && canPlayTop) return true;
                    if (activeTelephone.HasBottom && canPlayBottom) return true;
                }
                else
                {
                    if (canPlayTop) return true;
                    if (canPlayBottom) return true;
                }
            }

            return false;
        }

        public bool CanPlayerPlay(List<int[]> hand, TelephoneBoardState board)
        {
            return hand.Any(t => CanPlay(t[0], t[1], board));
        }

        public bool IsGameBlocked(Game game, List<GameParticipant> participants, TelephoneBoardState board)
        {
            if (board.Tiles.Count == 0) return false;

            var boneyard = ParseTiles(game.BoneyardJson);
            var playerCount = participants.Count;
            var playableCount = GetPlayableBoneyardCount(boneyard, playerCount);

            if (playableCount > 0)
            {
                foreach (var tile in boneyard.Take(playableCount))
                {
                    if (CanPlay(tile[0], tile[1], board))
                        return false;
                }
            }

            foreach (var p in participants)
            {
                var hand = ParseTiles(p.HandJson);
                if (CanPlayerPlay(hand, board))
                    return false;
            }

            return true;
        }

        public int GetUntouchableCount(int playerCount) => playerCount switch { 2 => 2, 3 => 1, _ => 0 };
        public int GetDrawCount(int playerCount) => playerCount switch { 2 => 2, 3 => 1, _ => 0 };
        public int GetPlayableBoneyardCount(List<int[]> boneyard, int playerCount)
            => Math.Max(0, boneyard.Count - GetUntouchableCount(playerCount));

        public int CalculateBoardPoints(TelephoneBoardState board)
        {
            if (board.Tiles.Count == 0) return 0;

            int sum = 0;
            var activeTelephone = board.Telephones.FirstOrDefault(t => t.IsClosed && (!t.HasTop || !t.HasBottom));

            if (board.Tiles.Count == 1)
            {
                var first = board.Tiles[0];
                return first.Left + first.Right;
            }

            var leftChainTiles = board.Tiles
                .Where(t => t.AttachedToDoubleIndex == null && (t.Position == "left" || t.Position == "center"))
                .ToList();
            var leftmostTile = leftChainTiles.Where(t => t.Position == "left").LastOrDefault()
                ?? leftChainTiles.FirstOrDefault(t => t.Position == "center");

            if (leftmostTile != null)
            {
                int tileIndex = board.Tiles.IndexOf(leftmostTile);
                bool isActiveTel = activeTelephone != null && tileIndex == activeTelephone.TileIndex;
                if (!isActiveTel)
                {
                    if (leftmostTile.Left == leftmostTile.Right)
                        sum += leftmostTile.Left * 2;
                    else
                        sum += board.LeftEnd ?? 0;
                }
            }

            var rightChainTiles = board.Tiles
                .Where(t => t.AttachedToDoubleIndex == null && (t.Position == "right" || t.Position == "center"))
                .ToList();
            var rightmostTile = rightChainTiles.LastOrDefault(t => t.Position == "right")
                ?? rightChainTiles.FirstOrDefault(t => t.Position == "center");

            if (rightmostTile != null)
            {
                int tileIndex = board.Tiles.IndexOf(rightmostTile);
                bool isActiveTel = activeTelephone != null && tileIndex == activeTelephone.TileIndex;
                if (!isActiveTel)
                {
                    if (rightmostTile.Left == rightmostTile.Right)
                        sum += rightmostTile.Left * 2;
                    else
                        sum += board.RightEnd ?? 0;
                }
            }

            foreach (var tel in board.Telephones.Where(t => t.IsClosed))
            {
                bool isActive = activeTelephone != null && tel.TileIndex == activeTelephone.TileIndex;

                if (tel.HasTop)
                {
                    var topTiles = board.Tiles
                        .Where(t => t.AttachedToDoubleIndex == tel.TileIndex && t.AttachedSide == "top")
                        .ToList();
                    var topEdgeTile = topTiles.LastOrDefault();

                    if (topEdgeTile != null)
                    {
                        if (topEdgeTile.Left == topEdgeTile.Right)
                            sum += topEdgeTile.Left * 2;
                        else
                            sum += tel.TopEnd ?? 0;
                    }
                }

                if (tel.HasBottom)
                {
                    var bottomTiles = board.Tiles
                        .Where(t => t.AttachedToDoubleIndex == tel.TileIndex && t.AttachedSide == "bottom")
                        .ToList();
                    var bottomEdgeTile = bottomTiles.LastOrDefault();

                    if (bottomEdgeTile != null)
                    {
                        if (bottomEdgeTile.Left == bottomEdgeTile.Right)
                            sum += bottomEdgeTile.Left * 2;
                        else
                            sum += tel.BottomEnd ?? 0;
                    }
                }
            }

            return sum;
        }

        public TelephonePlayResult ValidateAndPlay(TelephoneBoardState board, int tileLeft, int tileRight, string side, bool isFirstTile)
        {
            if (isFirstTile)
            {
                var newTile = new TelephoneTile
                {
                    Left = tileLeft,
                    Right = tileRight,
                    Position = "center",
                    IsFlipped = false
                };
                board.Tiles.Add(newTile);

                if (tileLeft == tileRight)
                {
                    board.LeftEnd = tileLeft;
                    board.RightEnd = tileLeft;
                    board.Telephones.Add(new TelephoneDouble
                    {
                        Value = tileLeft,
                        TileIndex = 0,
                        IsClosed = false
                    });
                }
                else
                {
                    board.LeftEnd = tileLeft;
                    board.RightEnd = tileRight;
                }

                int points = CalculateBoardPoints(board);
                int awarded = points % 5 == 0 ? points : 0;

                bool is23 = (tileLeft == 2 && tileRight == 3) || (tileLeft == 3 && tileRight == 2);
                if (is23) awarded = 0;

                return new TelephonePlayResult(true, null, board, awarded);
            }

            bool isDouble = tileLeft == tileRight;

            if (side == "left" || side == "right")
            {
                int targetEnd = side == "left" ? board.LeftEnd!.Value : board.RightEnd!.Value;

                if (tileLeft != targetEnd && tileRight != targetEnd)
                    return new TelephonePlayResult(false, $"Tile cannot connect to {side}", board, 0);

                int newEnd;
                bool isFlipped;

                if (side == "left")
                {
                    if (tileRight == targetEnd)
                    {
                        newEnd = tileLeft;
                        isFlipped = false;
                    }
                    else
                    {
                        newEnd = tileRight;
                        isFlipped = true;
                    }
                }
                else
                {
                    if (tileLeft == targetEnd)
                    {
                        newEnd = tileRight;
                        isFlipped = false;
                    }
                    else
                    {
                        newEnd = tileLeft;
                        isFlipped = true;
                    }
                }

                var newTile = new TelephoneTile
                {
                    Left = tileLeft,
                    Right = tileRight,
                    Position = side,
                    IsFlipped = isFlipped
                };
                board.Tiles.Add(newTile);

                if (side == "left")
                    board.LeftEnd = newEnd;
                else
                    board.RightEnd = newEnd;

                var targetDouble = board.Telephones.FirstOrDefault(t => t.Value == targetEnd && !t.IsClosed);
                if (targetDouble != null)
                {
                    int doubleIndex = targetDouble.TileIndex;
                    var doubleTile = board.Tiles[doubleIndex];

                    bool hasLeft = board.Tiles.Any(t => t.Position == "left" && board.Tiles.IndexOf(t) != doubleIndex);
                    bool hasRight = board.Tiles.Any(t => t.Position == "right" && board.Tiles.IndexOf(t) != doubleIndex);

                    if (doubleTile.Position == "center")
                    {
                        hasLeft = board.Tiles.Any(t => t.Position == "left");
                        hasRight = board.Tiles.Any(t => t.Position == "right");
                    }

                    if (hasLeft && hasRight && !board.Telephones.Any(x => x.IsClosed))
                        targetDouble.IsClosed = true;
                }

                if (isDouble)
                {
                    board.Telephones.Add(new TelephoneDouble
                    {
                        Value = tileLeft,
                        TileIndex = board.Tiles.Count - 1,
                        IsClosed = false
                    });
                }
            }
            else if (side == "top" || side == "bottom")
            {
                var activeTelephone = board.Telephones.FirstOrDefault(t => t.IsClosed);
                if (activeTelephone == null)
                    return new TelephonePlayResult(false, "No active telephone", board, 0);

                int targetValue;
                if (side == "top")
                    targetValue = activeTelephone.HasTop ? (activeTelephone.TopEnd ?? activeTelephone.Value) : activeTelephone.Value;
                else
                    targetValue = activeTelephone.HasBottom ? (activeTelephone.BottomEnd ?? activeTelephone.Value) : activeTelephone.Value;

                bool isFirstExtension = (side == "top" && !activeTelephone.HasTop) || (side == "bottom" && !activeTelephone.HasBottom);

                if (isDouble && isFirstExtension)
                    return new TelephonePlayResult(false, "First telephone extension cannot be a double", board, 0);

                if (activeTelephone == null)
                    return new TelephonePlayResult(false, "No active telephone", board, 0);

                if (tileLeft != targetValue && tileRight != targetValue)
                    return new TelephonePlayResult(false, $"Tile cannot connect to {side} (needs {targetValue})", board, 0);

                int newEnd;
                bool isFlipped;

                if (side == "top")
                {
                    if (tileRight == targetValue)
                    {
                        newEnd = tileLeft;
                        isFlipped = false;
                    }
                    else
                    {
                        newEnd = tileRight;
                        isFlipped = true;
                    }
                }
                else
                {
                    if (tileLeft == targetValue)
                    {
                        newEnd = tileRight;
                        isFlipped = false;
                    }
                    else
                    {
                        newEnd = tileLeft;
                        isFlipped = true;
                    }
                }

                var newTile = new TelephoneTile
                {
                    Left = tileLeft,
                    Right = tileRight,
                    Position = side,
                    AttachedToDoubleIndex = activeTelephone.TileIndex,
                    AttachedSide = side,
                    IsFlipped = isFlipped
                };
                board.Tiles.Add(newTile);

                if (side == "top")
                {
                    activeTelephone.HasTop = true;
                    activeTelephone.TopEnd = newEnd;
                }
                else
                {
                    activeTelephone.HasBottom = true;
                    activeTelephone.BottomEnd = newEnd;
                }
                if (isDouble)
                {
                    board.Telephones.Add(new TelephoneDouble
                    {
                        Value = tileLeft,
                        TileIndex = board.Tiles.Count - 1,
                        IsClosed = false,
                        HasTop = false,
                        HasBottom = false
                    });
                }
            }

            int boardPoints = CalculateBoardPoints(board);
            int pointsAwarded = boardPoints % 5 == 0 ? boardPoints : 0;

            return new TelephonePlayResult(true, null, board, pointsAwarded);
        }

        public List<int[][]>? FindComboPlay(List<int[]> hand, TelephoneBoardState board)
        {
            var doubles = hand.Where(t => t[0] == t[1]).ToList();
            if (doubles.Count < 2) return null;

            for (int i = 0; i < doubles.Count; i++)
            {
                for (int j = i + 1; j < doubles.Count; j++)
                {
                    var d1 = doubles[i];
                    var d2 = doubles[j];

                    var tempBoard1 = CloneBoard(board);
                    var result1 = TryPlayDouble(tempBoard1, d1);
                    if (!result1.success) continue;

                    var tempBoard2 = CloneBoard(result1.board);
                    var result2 = TryPlayDouble(tempBoard2, d2);
                    if (!result2.success) continue;

                    int comboPoints = CalculateBoardPoints(result2.board);
                    if (comboPoints % 5 == 0)
                        return new List<int[][]> { new[] { d1, d2 } };
                }
            }

            return null;
        }

        private (bool success, TelephoneBoardState board) TryPlayDouble(TelephoneBoardState board, int[] tile)
        {
            if (tile[0] == board.LeftEnd)
            {
                var result = ValidateAndPlay(board, tile[0], tile[1], "left", false);
                return (result.IsValid, result.Board);
            }
            if (tile[0] == board.RightEnd)
            {
                var result = ValidateAndPlay(board, tile[0], tile[1], "right", false);
                return (result.IsValid, result.Board);
            }
            return (false, board);
        }

        private TelephoneBoardState CloneBoard(TelephoneBoardState board)
        {
            return new TelephoneBoardState
            {
                LeftEnd = board.LeftEnd,
                RightEnd = board.RightEnd,
                Tiles = board.Tiles.Select(t => new TelephoneTile
                {
                    Left = t.Left,
                    Right = t.Right,
                    Position = t.Position,
                    AttachedToDoubleIndex = t.AttachedToDoubleIndex,
                    AttachedSide = t.AttachedSide,
                    IsFlipped = t.IsFlipped,
                }).ToList(),
                Telephones = board.Telephones.Select(tel => new TelephoneDouble
                {
                    Value = tel.Value,
                    TileIndex = tel.TileIndex,
                    IsClosed = tel.IsClosed,
                    TopEnd = tel.TopEnd,
                    BottomEnd = tel.BottomEnd,
                    HasTop = tel.HasTop,
                    HasBottom = tel.HasBottom
                }).ToList()
            };
        }

        public TelephoneRoundResult DetermineRoundWinner(Game game, List<GameParticipant> participants, Guid? blockerId)
        {
            var dominoWinner = participants.FirstOrDefault(p => ParseTiles(p.HandJson).Count == 0);

            if (dominoWinner != null)
            {
                int othersSum = participants
                    .Where(p => p.Id != dominoWinner.Id)
                    .Sum(p => CalculateHandScore(ParseTiles(p.HandJson)));

                int pointsWon = CalculateWinPoints(dominoWinner.TotalScore, othersSum);

                if (pointsWon > 0 && pointsWon % 5 != 0)
                {
                    pointsWon = ((pointsWon / 5) + 1) * 5;
                }

                return new TelephoneRoundResult(
                    dominoWinner.UserId,
                    pointsWon,
                    "domino",
                    dominoWinner.UserId,
                    false,
                    0);
            }

            var playerScores = participants.Select(p => new
            {
                p.UserId,
                Score = CalculateHandScore(ParseTiles(p.HandJson))
            }).ToList();

            var minScore = playerScores.Min(x => x.Score);
            var winners = playerScores.Where(x => x.Score == minScore).ToList();

            if (winners.Count > 1)
            {
                var nextStarter = blockerId ?? participants.OrderBy(p => p.Position).First().UserId;
                return new TelephoneRoundResult(null, 0, "tie", nextStarter, true, minScore);
            }

            var winner = participants.First(p => p.UserId == winners[0].UserId);
            int othersTotal = playerScores.Where(x => x.UserId != winner.UserId).Sum(x => x.Score);
            int roundPoints = othersTotal;

            if (roundPoints > 0 && roundPoints % 5 != 0)
                roundPoints = 5 - (roundPoints % 5) + roundPoints;

            return new TelephoneRoundResult(winner.UserId, roundPoints, "block", winner.UserId, false, 0);
        }

        private int CalculateWinPoints(int currentScore, int othersSum)
        {
            int rounded = ((5 - othersSum % 5) % 5) + othersSum;

            if (currentScore >= PointCapThreshold)
            {
                int remaining = WinScore - currentScore;
                return remaining > 0 ? Math.Min(remaining, rounded) : 0;
            }

            int newTotal = currentScore + rounded;
            if (newTotal > WinScore)
                return WinScore - currentScore;

            return rounded;
        }

        public bool CheckMatchWin(List<GameParticipant> participants)
        {
            return participants.Any(p => p.TotalScore >= WinScore);
        }

        public string AssignColor(int position) => PlayerColors[position % PlayerColors.Length];

        public static List<int[]> ParseTiles(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<int[]>();
            try { return JsonSerializer.Deserialize<List<int[]>>(json) ?? new List<int[]>(); }
            catch { return new List<int[]>(); }
        }

        public static string SerializeTiles(List<int[]> tiles) => JsonSerializer.Serialize(tiles);

        public static TelephoneBoardState ParseBoardState(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new TelephoneBoardState();
            try { return JsonSerializer.Deserialize<TelephoneBoardState>(json) ?? new TelephoneBoardState(); }
            catch { return new TelephoneBoardState(); }
        }

        public static string SerializeBoardState(TelephoneBoardState board) => JsonSerializer.Serialize(board);
    }

    public sealed record TelephonePlayResult(
        bool IsValid,
        string? Error,
        TelephoneBoardState Board,
        int PointsAwarded);

    public sealed record TelephoneRoundResult(
        Guid? WinnerId,
        int PointsWon,
        string Reason,
        Guid? NextRoundStarterId,
        bool IsTie,
        int TiedSum);
}