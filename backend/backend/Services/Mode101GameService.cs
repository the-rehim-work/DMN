using backend.Data;
using System.Text.Json;

namespace backend.Services
{
    public interface IMode101GameService
    {
        List<int[]> GenerateFullSet();
        void ShuffleTiles(List<int[]> tiles);
        int CalculateHandScore(List<int[]> hand);
        bool CanPlay(int tileLeft, int tileRight, int? boardLeft, int? boardRight);
        bool CanPlayerPlay(List<int[]> hand, int? boardLeft, int? boardRight);
        bool IsGameBlocked(Game game, List<GameParticipant> participants);
        int GetUntouchableCount(int playerCount);
        int GetDrawCount(int playerCount);
        int GetPlayableBoneyardCount(List<int[]> boneyard, int playerCount);
        PlayValidationResult ValidatePlay(int? boardLeft, int? boardRight, int tileLeft, int tileRight, string side, bool isFirstTile, bool isFirstRound);
        RoundResult DetermineRoundWinner(Game game, List<GameParticipant> participants, Guid? blockerId);
        bool CheckMatchWin(int team1Score, int team2Score, List<GameParticipant> participants, bool isTeamGame);
        string AssignColor(int position);
        int FindFirstRoundStarter(List<GameParticipant> participants);
        int[] GetRequiredFirstTile(List<GameParticipant> participants);
        bool ShouldReshuffleHand(List<int[]> hand);
    }

    public sealed class Mode101GameService : IMode101GameService
    {
        private static readonly string[] PlayerColors = { "#e63946", "#2a9d8f", "#e9c46a", "#9b5de5" };
        private const int WinScore = 101;
        private const int LowWinThreshold = 13;
        private const int ConsecutiveLowWinsReset = 3;

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

            bool hasOnlyDoubleZero = hand.Count == 1
                && hand[0][0] == 0
                && hand[0][1] == 0;

            if (hasOnlyDoubleZero) return 10;

            int score = 0;
            foreach (var tile in hand)
            {
                score += tile[0] + tile[1];
            }
            return score;
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

        public bool CanPlay(int tileLeft, int tileRight, int? boardLeft, int? boardRight)
        {
            if (boardLeft == null || boardRight == null)
                return true;

            return tileLeft == boardLeft || tileRight == boardLeft ||
                   tileLeft == boardRight || tileRight == boardRight;
        }

        public bool CanPlayerPlay(List<int[]> hand, int? boardLeft, int? boardRight)
        {
            return hand.Any(t => CanPlay(t[0], t[1], boardLeft, boardRight));
        }

        public int GetUntouchableCount(int playerCount)
        {
            return playerCount switch
            {
                2 => 2,
                3 => 1,
                _ => 0
            };
        }

        public int GetDrawCount(int playerCount)
        {
            return playerCount switch
            {
                2 => 2,
                3 => 1,
                _ => 0
            };
        }

        public int GetPlayableBoneyardCount(List<int[]> boneyard, int playerCount)
        {
            var untouchable = GetUntouchableCount(playerCount);
            return Math.Max(0, boneyard.Count - untouchable);
        }

        public bool IsGameBlocked(Game game, List<GameParticipant> participants)
        {
            if (game.BoardLeft == null || game.BoardRight == null)
                return false;

            var boneyard = ParseTiles(game.BoneyardJson);
            var playerCount = participants.Count;
            var playableCount = GetPlayableBoneyardCount(boneyard, playerCount);

            if (playableCount > 0)
            {
                foreach (var tile in boneyard.Take(playableCount))
                {
                    if (CanPlay(tile[0], tile[1], game.BoardLeft, game.BoardRight))
                        return false;
                }
            }

            foreach (var p in participants)
            {
                var hand = ParseTiles(p.HandJson);
                if (CanPlayerPlay(hand, game.BoardLeft, game.BoardRight))
                    return false;
            }

            return true;
        }

        public PlayValidationResult ValidatePlay(
            int? boardLeft,
            int? boardRight,
            int tileLeft,
            int tileRight,
            string side,
            bool isFirstTile,
            bool isFirstRound)
        {
            if (isFirstTile && isFirstRound)
            {
                if (tileLeft != tileRight || tileLeft < 1 || tileLeft > 6)
                {
                    return new PlayValidationResult(false, "First round must start with a double", 0, 0, 0, false);
                }

                return new PlayValidationResult(true, null, tileLeft, tileRight, tileLeft, false);
            }

            if (isFirstTile)
            {
                return new PlayValidationResult(true, null, tileLeft, tileRight,
                    side == "left" ? tileLeft : tileRight, false);
            }

            int targetEnd = side == "left" ? boardLeft!.Value : boardRight!.Value;

            if (tileLeft == targetEnd)
            {
                int newEnd = tileRight;
                return new PlayValidationResult(true, null,
                    side == "left" ? tileRight : tileLeft,
                    side == "left" ? tileLeft : tileRight,
                    newEnd, side == "left");
            }

            if (tileRight == targetEnd)
            {
                int newEnd = tileLeft;
                return new PlayValidationResult(true, null,
                    side == "left" ? tileLeft : tileRight,
                    side == "left" ? tileRight : tileLeft,
                    newEnd, side != "left");
            }

            return new PlayValidationResult(false,
                $"Tile [{tileLeft}|{tileRight}] cannot be played on {side} (needs {targetEnd})",
                0, 0, 0, false);
        }



        public RoundResult DetermineRoundWinner(Game game, List<GameParticipant> participants, Guid? blockerId)
        {
            var dominoWinner = participants.FirstOrDefault(p => ParseTiles(p.HandJson).Count == 0);

            if (dominoWinner != null)
            {
                int pointsWon;
                if (game.IsTeamGame)
                {
                    pointsWon = participants
                        .Where(p => p.Team != dominoWinner.Team)
                        .Sum(p => CalculateHandScore(ParseTiles(p.HandJson)));
                }
                else
                {
                    pointsWon = participants
                        .Where(p => p.Id != dominoWinner.Id)
                        .Sum(p => CalculateHandScore(ParseTiles(p.HandJson)));
                }

                return new RoundResult(
                    dominoWinner.UserId,
                    game.IsTeamGame ? dominoWinner.Team : null,
                    pointsWon,
                    "domino",
                    dominoWinner.UserId,
                    false,
                    0);
            }

            if (game.IsTeamGame)
            {
                var team1Sum = participants.Where(p => p.Team == 1).Sum(p => CalculateHandScore(ParseTiles(p.HandJson)));
                var team2Sum = participants.Where(p => p.Team == 2).Sum(p => CalculateHandScore(ParseTiles(p.HandJson)));

                if (team1Sum == team2Sum)
                {
                    var nextStarter = blockerId ?? participants.OrderBy(p => p.Position).First().UserId;
                    return new RoundResult(null, null, 0, "tie", nextStarter, true, team1Sum);
                }

                if (team1Sum < team2Sum)
                {
                    var pointsWon = team2Sum - team1Sum;
                    var nextStarter = blockerId.HasValue &&
                        participants.Any(p => p.UserId == blockerId && p.Team == 1)
                        ? blockerId.Value
                        : participants.Where(p => p.Team == 1).OrderBy(p => p.Position).First().UserId;

                    return new RoundResult(nextStarter, 1, pointsWon, "block", nextStarter, false, 0);
                }
                else
                {
                    var pointsWon = team1Sum - team2Sum;
                    var nextStarter = blockerId.HasValue &&
                        participants.Any(p => p.UserId == blockerId && p.Team == 2)
                        ? blockerId.Value
                        : participants.Where(p => p.Team == 2).OrderBy(p => p.Position).First().UserId;

                    return new RoundResult(nextStarter, 2, pointsWon, "block", nextStarter, false, 0);
                }
            }
            else
            {
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
                    return new RoundResult(null, null, 0, "tie", nextStarter, true, minScore);
                }

                var totalOthers = playerScores.Where(x => x.UserId != winners[0].UserId).Sum(x => x.Score);
                var pointsWon = totalOthers - minScore;

                return new RoundResult(winners[0].UserId, null, pointsWon, "block", winners[0].UserId, false, 0);
            }
        }

        public bool CheckMatchWin(int team1Score, int team2Score, List<GameParticipant> participants, bool isTeamGame)
        {
            if (isTeamGame)
            {
                return team1Score >= WinScore || team2Score >= WinScore;
            }
            else
            {
                return participants.Any(p => p.TotalScore >= WinScore);
            }
        }

        public string AssignColor(int position)
        {
            return PlayerColors[position % PlayerColors.Length];
        }

        public int FindFirstRoundStarter(List<GameParticipant> participants)
        {
            int[][] priorityTiles = new[]
            {
                new[] { 1, 1 },
                new[] { 2, 2 },
                new[] { 3, 3 },
                new[] { 4, 4 },
                new[] { 5, 5 },
                new[] { 6, 6 }
            };

            foreach (var tile in priorityTiles)
            {
                foreach (var p in participants.OrderBy(x => x.Position))
                {
                    var hand = ParseTiles(p.HandJson);
                    if (hand.Any(t => t[0] == tile[0] && t[1] == tile[1]))
                    {
                        return p.Position;
                    }
                }
            }

            return participants.OrderBy(x => x.Position).First().Position;
        }

        public int[] GetRequiredFirstTile(List<GameParticipant> participants)
        {
            int[][] priorityTiles = new[]
            {
                new[] { 1, 1 },
                new[] { 2, 2 },
                new[] { 3, 3 },
                new[] { 4, 4 },
                new[] { 5, 5 },
                new[] { 6, 6 }
            };

            foreach (var tile in priorityTiles)
            {
                foreach (var p in participants.OrderBy(x => x.Position))
                {
                    var hand = ParseTiles(p.HandJson);
                    if (hand.Any(t => t[0] == tile[0] && t[1] == tile[1]))
                    {
                        return tile;
                    }
                }
            }

            return new[] { 1, 1 };
        }

        public static List<int[]> ParseTiles(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<int[]>();
            try { return JsonSerializer.Deserialize<List<int[]>>(json) ?? new List<int[]>(); }
            catch { return new List<int[]>(); }
        }

        public static string SerializeTiles(List<int[]> tiles)
            => JsonSerializer.Serialize(tiles);

        public static List<Guid> ParseVotes(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<Guid>();
            try { return JsonSerializer.Deserialize<List<Guid>>(json) ?? new List<Guid>(); }
            catch { return new List<Guid>(); }
        }

        public static string SerializeVotes(List<Guid> votes)
            => JsonSerializer.Serialize(votes);
    }

    public sealed record PlayValidationResult(
        bool IsValid,
        string? Error,
        int OrientedLeft,
        int OrientedRight,
        int NewBoardEnd,
        bool IsFlipped);

    public sealed record RoundResult(
        Guid? WinnerId,
        int? WinningTeam,
        int PointsWon,
        string Reason,
        Guid? NextRoundStarterId,
        bool IsTie,
        int TiedSum
    );


}