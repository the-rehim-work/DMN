using backend.Data;
using backend.Hubs;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace backend.Controllers
{
    [ApiController]
    [Route("api")]
    public sealed class GamesController : ControllerBase
    {
        private readonly AppDb _db;
        private readonly IHubContext<GameHub> _hub;
        private readonly UserManager<ApplicationUser> _users;
        private readonly IMode101GameService _gameService;

        public GamesController(
            AppDb db,
            IHubContext<GameHub> hub,
            UserManager<ApplicationUser> users,
            IMode101GameService gameService)
        {
            _db = db;
            _hub = hub;
            _users = users;
            _gameService = gameService;
        }

        [HttpPost("games")]
        [Authorize]
        public async Task<IActionResult> CreateGame([FromBody] CreateGameDto? dto)
        {
            var maxPlayers = dto?.MaxPlayers ?? 4;
            var minPlayers = dto?.MinPlayers ?? 2;
            var isTeamGame = dto?.IsTeamGame ?? false;

            if (maxPlayers < 2 || maxPlayers > 4)
                return BadRequest(new { message = "MaxPlayers must be 2-4" });
            if (minPlayers < 2 || minPlayers > maxPlayers)
                return BadRequest(new { message = "MinPlayers must be 2 to MaxPlayers" });
            if (isTeamGame && maxPlayers != 4)
                return BadRequest(new { message = "Team game requires exactly 4 players" });

            var isAnonymous = dto?.IsAnonymous ?? false;
            var code = ShortCode();
            var game = new Game
            {
                Code = code,
                Mode = "mode101",
                MaxPlayers = maxPlayers,
                MinPlayers = minPlayers,
                IsTeamGame = isTeamGame,
                IsAnonymous = isAnonymous,
                Status = "waiting"
            };

            _db.Games.Add(game);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                game.Id,
                game.Code,
                game.Mode,
                game.Status,
                game.MaxPlayers,
                game.MinPlayers,
                game.IsTeamGame
            });
        }

        [HttpGet("games/{id:guid}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetGame(Guid id)
        {
            var g = await _db.Games
                .Include(x => x.Participants).ThenInclude(p => p.User)
                .Include(x => x.Board.Where(b => b.RoundNumber == _db.Games.First(ga => ga.Id == id).RoundNumber).OrderBy(b => b.Index))
                .Include(x => x.Moves.OrderBy(m => m.MoveIndex))
                .FirstOrDefaultAsync(x => x.Id == id);

            if (g is null) return NotFound();

            ApplicationUser? me = null;
            if (User?.Identity?.IsAuthenticated == true)
                me = await _users.GetUserAsync(User);

            var myParticipant = me != null
                ? g.Participants.FirstOrDefault(p => p.UserId == me.Id)
                : null;

            var currentRoundBoard = g.Board.Where(b => b.RoundNumber == g.RoundNumber).OrderBy(b => b.Index).ToList();
            var boneyard = Mode101GameService.ParseTiles(g.BoneyardJson);

            var dto = new
            {
                g.Id,
                g.Code,
                g.Mode,
                g.Status,
                g.MaxPlayers,
                g.MinPlayers,
                g.CurrentTurn,
                g.BoardLeft,
                g.BoardRight,
                g.Outcome,
                g.Reason,
                g.WinnerId,
                g.WinningTeam,
                g.RoundNumber,
                g.IsTeamGame,
                g.Team1Score,
                g.Team2Score,
                g.DeferredPoints,
                g.CreatedAt,
                g.StartedAt,
                g.EndedAt,
                BoneyardCount = boneyard.Count,
                UntouchableCount = _gameService.GetUntouchableCount(g.Participants.Count),
                PlayableBoneyardCount = _gameService.GetPlayableBoneyardCount(boneyard, g.Participants.Count),
                Participants = g.Participants
                    .OrderBy(p => p.Position)
                    .Select(p => new
                    {
                        p.UserId,
                        DisplayName = g.IsAnonymous && g.Status != "finished"
                            ? $"Player {p.Position + 1}"
                            : p.User.DisplayName,
                        p.Position,
                        p.Team,
                        p.Color,
                        p.TotalScore,
                        p.RoundScore,
                        p.HasVotedToStart,
                        TileCount = Mode101GameService.ParseTiles(p.HandJson).Count,
                        IsCurrentTurn = g.CurrentTurn == p.Position
                    })
                    .ToList(),
                Board = currentRoundBoard
                    .Select(b => new
                    {
                        b.Left,
                        b.Right,
                        b.Side,
                        b.IsFlipped,
                        b.PlayedById,
                        b.PlayedByPosition,
                        b.PlayedByColor
                    })
                    .ToList(),
                MyHand = myParticipant != null
                    ? Mode101GameService.ParseTiles(myParticipant.HandJson)
                    : new List<int[]>(),
                AllHands = g.Status == "round_end" || g.Status == "finished"
                    ? g.Participants.Select(p => new
                    {
                        p.UserId,
                        p.Position,
                        Hand = Mode101GameService.ParseTiles(p.HandJson)
                    }).ToList()
                    : null,
                MyPosition = myParticipant?.Position,
                MyTeam = myParticipant?.Team,
                RequiredFirstTile = g.RoundNumber == 1 && g.Status == "active" && currentRoundBoard.Count == 0
                    ? _gameService.GetRequiredFirstTile(g.Participants.ToList())
                    : null,
                RoundHistory = string.IsNullOrEmpty(g.RoundHistoryJson)
                    ? new List<object>()
                    : JsonSerializer.Deserialize<List<object>>(g.RoundHistoryJson),
                History = g.Moves
                    .Where(m => m.RoundNumber == g.RoundNumber)
                    .OrderBy(m => m.MoveIndex)
                    .Select(m => new
                    {
                        m.MoveIndex,
                        m.MoveType,
                        m.TileLeft,
                        m.TileRight,
                        m.PlayedSide,
                        m.DrawnTilesJson,
                        m.PointsGained,
                        m.PlayerId
                    })
                    .ToList()
            };

            return Ok(dto);
        }

        [HttpGet("games/by-code/{code}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetGameByCode(string code)
        {
            var g = await _db.Games.FirstOrDefaultAsync(x => x.Code == code.ToUpper());
            if (g is null) return NotFound();
            return Ok(new { g.Id, g.Code, g.Mode, g.Status });
        }

        [HttpGet("games")]
        [Authorize]
        public async Task<IActionResult> GetGames(
            [FromQuery] string status = "all",
            [FromQuery] bool onlyMine = false,
            [FromQuery] string? q = null)
        {
            var me = await _users.GetUserAsync(User);

            IQueryable<Game> query = _db.Games
                .Include(x => x.Participants).ThenInclude(p => p.User);

            if (status is "waiting" or "voting" or "active" or "round_end" or "finished")
                query = query.Where(x => x.Status == status);

            if (onlyMine)
                query = query.Where(x => x.Participants.Any(p => p.UserId == me!.Id));

            if (!string.IsNullOrWhiteSpace(q))
            {
                var ql = q.ToLower();
                query = query.Where(x =>
                    x.Code.ToLower().Contains(ql) ||
                    x.Participants.Any(p => (p.User.DisplayName ?? p.User.UserName!).ToLower().Contains(ql)));
            }

            var rows = await query
                .OrderByDescending(x => x.CreatedAt)
                .Take(50)
                .Select(x => new
                {
                    x.Id,
                    x.Code,
                    x.Mode,
                    x.Status,
                    x.MaxPlayers,
                    x.MinPlayers,
                    x.IsTeamGame,
                    x.RoundNumber,
                    x.Team1Score,
                    x.Team2Score,
                    x.Outcome,
                    x.Reason,
                    x.CreatedAt,
                    Participants = x.Participants
                        .OrderBy(p => p.Position)
                        .Select(p => new
                        {
                            p.UserId,
                            DisplayName = x.IsAnonymous && x.Status != "finished"
                                ? $"Player {p.Position + 1}"
                                : p.User.DisplayName,
                            p.Position,
                            p.Team,
                            p.Color,
                            p.TotalScore,
                            p.HasVotedToStart
                        })
                        .ToList(),
                    Perspective = new
                    {
                        IsParticipant = x.Participants.Any(p => p.UserId == me!.Id),
                        MyPosition = x.Participants
                            .Where(p => p.UserId == me!.Id)
                            .Select(p => (int?)p.Position)
                            .FirstOrDefault(),
                        CanJoin = x.Status == "waiting" && x.Participants.Count < x.MaxPlayers,
                        IsFull = x.Participants.Count >= x.MaxPlayers
                    }
                })
                .ToListAsync();

            return Ok(rows);
        }

        [HttpPost("games/{id:guid}/join")]
        [Authorize]
        public async Task<IActionResult> JoinGame(Guid id, [FromQuery] int? position = null)
        {
            var g = await _db.Games
                .Include(x => x.Participants)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (g is null) return NotFound();

            var user = await _users.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var existing = g.Participants.FirstOrDefault(p => p.UserId == user.Id);
            if (existing != null)
                return Ok(new { g.Id, g.Code, position = existing.Position, existing.Team, existing.Color, g.Status });

            if (g.Participants.Count >= g.MaxPlayers)
                return Ok(new { g.Id, g.Code, role = "spectator", g.Status, message = "Game is full" });

            if (g.Status != "waiting")
                return BadRequest(new { message = "Cannot join; game already started" });

            if (g.IsTeamGame)
            {
                var hasActiveTeamGame = await _db.GameParticipants
                    .AnyAsync(gp => gp.UserId == user.Id
                        && gp.GameId != id
                        && gp.Game.IsTeamGame
                        && gp.Game.Status != "finished");

                if (hasActiveTeamGame)
                    return BadRequest(new { message = "You already have an active team game. Finish or leave it first." });
            }

            var takenPositions = g.Participants.Select(p => p.Position).ToHashSet();
            int chosenPosition;

            if (position.HasValue && position >= 0 && position < g.MaxPlayers && !takenPositions.Contains(position.Value))
            {
                chosenPosition = position.Value;
            }
            else
            {
                chosenPosition = Enumerable.Range(0, g.MaxPlayers).First(p => !takenPositions.Contains(p));
            }

            var team = g.IsTeamGame ? (chosenPosition % 2) + 1 : 0;
            var color = _gameService.AssignColor(chosenPosition);

            var gp = new GameParticipant
            {
                GameId = g.Id,
                UserId = user.Id,
                Position = chosenPosition,
                Team = team,
                Color = color
            };
            _db.GameParticipants.Add(gp);
            await _db.SaveChangesAsync();

            await _hub.Clients.Group(g.Id.ToString()).SendAsync("game:update", new
            {
                type = "join",
                gameId = g.Id,
                player = new
                {
                    userId = user.Id,
                    displayName = user.DisplayName ?? user.UserName,
                    position = chosenPosition,
                    team,
                    color
                },
                playerCount = g.Participants.Count + 1,
                status = g.Status
            });

            return Ok(new { g.Id, g.Code, position = chosenPosition, team, color, g.Status });
        }

        [HttpPost("games/{id:guid}/vote-start")]
        [Authorize]
        public async Task<IActionResult> VoteToStart(Guid id)
        {
            var g = await _db.Games
                .Include(x => x.Participants)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (g is null) return NotFound();

            var user = await _users.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var gp = g.Participants.FirstOrDefault(p => p.UserId == user.Id);
            if (gp is null) return Forbid();

            if (g.Status != "waiting" && g.Status != "voting")
                return BadRequest(new { message = "Game already started" });

            if (g.Participants.Count < g.MinPlayers)
                return BadRequest(new { message = $"Need at least {g.MinPlayers} players" });

            gp.HasVotedToStart = true;
            g.Status = "voting";

            var allVoted = g.Participants.All(p => p.HasVotedToStart);

            await _db.SaveChangesAsync();

            await _hub.Clients.Group(g.Id.ToString()).SendAsync("game:update", new
            {
                type = "vote",
                gameId = g.Id,
                playerId = user.Id,
                playerName = user.DisplayName ?? user.UserName,
                votedCount = g.Participants.Count(p => p.HasVotedToStart),
                totalPlayers = g.Participants.Count,
                allVoted
            });

            if (allVoted)
            {
                return await StartRound(id);
            }

            return Ok(new
            {
                voted = true,
                allVoted,
                votedCount = g.Participants.Count(p => p.HasVotedToStart)
            });
        }

        [HttpPost("games/{id:guid}/start-round")]
        [Authorize]
        public async Task<IActionResult> StartRound(Guid id)
        {
            var g = await _db.Games
                .Include(x => x.Participants)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (g is null) return NotFound();

            var user = await _users.GetUserAsync(User);
            if (user is null) return Unauthorized();

            if (!g.Participants.Any(p => p.UserId == user.Id))
                return Forbid();

            if (g.Status != "voting" && g.Status != "round_end")
                return BadRequest(new { message = "Cannot start round now" });

            var isFirstRound = g.RoundNumber == 0;
            var tiles = _gameService.GenerateFullSet();
            _gameService.ShuffleTiles(tiles);

            const int tilesPerPlayer = 7;
            var playerCount = g.Participants.Count;
            int starterIndex = 0;

            var participants = g.Participants.OrderBy(p => p.Position).ToList();
            var tileIndex = 0;

            const int maxReshuffleAttempts = 10;
            var reshuffleAttempt = 0;
            bool needsReshuffle;

            do
            {
                needsReshuffle = false;
                tileIndex = 0;

                foreach (var p in participants)
                {
                    var hand = tiles.Skip(tileIndex).Take(tilesPerPlayer).ToList();
                    tileIndex += tilesPerPlayer;
                    p.HandJson = Mode101GameService.SerializeTiles(hand);
                    p.RoundScore = 0;
                    p.HasVotedToStart = false;

                    if (_gameService.ShouldReshuffleHand(hand))
                        needsReshuffle = true;
                }

                if (needsReshuffle && reshuffleAttempt < maxReshuffleAttempts)
                {
                    reshuffleAttempt++;
                    tiles = _gameService.GenerateFullSet();
                    _gameService.ShuffleTiles(tiles);
                }
            } while (needsReshuffle && reshuffleAttempt < maxReshuffleAttempts);

            if (isFirstRound)
            {
                var maxAttempts = 10;
                var attempt = 0;

                while (attempt < maxAttempts)
                {
                    var requiredTile = _gameService.GetRequiredFirstTile(participants);
                    var tempBoneyard = tiles.Skip(tileIndex).ToList();

                    var requiredInBoneyard = tempBoneyard.Any(t =>
                        t[0] == requiredTile[0] && t[1] == requiredTile[1]);

                    if (!requiredInBoneyard)
                    {
                        starterIndex = _gameService.FindFirstRoundStarter(participants);
                        break;
                    }

                    tiles = _gameService.GenerateFullSet();
                    _gameService.ShuffleTiles(tiles);
                    tileIndex = 0;

                    foreach (var p in participants)
                    {
                        var hand = tiles.Skip(tileIndex).Take(tilesPerPlayer).ToList();
                        tileIndex += tilesPerPlayer;
                        p.HandJson = Mode101GameService.SerializeTiles(hand);
                    }

                    attempt++;
                }

                if (attempt == maxAttempts)
                {
                    starterIndex = _gameService.FindFirstRoundStarter(participants);
                }
            }

            if (!isFirstRound && g.RoundStarterId.HasValue)
            {
                var starter = participants.FirstOrDefault(p => p.UserId == g.RoundStarterId);
                if (starter != null) starterIndex = starter.Position;
            }

            var boneyard = tiles.Skip(tileIndex).ToList();
            g.BoneyardJson = Mode101GameService.SerializeTiles(boneyard);

            g.CurrentTurn = starterIndex;
            g.Status = "active";
            g.RoundNumber++;
            g.BoardLeft = null;
            g.BoardRight = null;

            if (isFirstRound)
            {
                g.StartedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();

            await _hub.Clients.Group(g.Id.ToString()).SendAsync("game:update", new
            {
                type = "round_start",
                gameId = g.Id,
                roundNumber = g.RoundNumber,
                currentTurn = g.CurrentTurn,
                starterPosition = starterIndex,
                boneyardCount = boneyard.Count,
                status = g.Status
            });

            foreach (var p in participants)
            {
                var hand = Mode101GameService.ParseTiles(p.HandJson);
                await _hub.Clients.User(p.UserId.ToString()).SendAsync("game:private", new
                {
                    type = "hand",
                    hand
                });
            }

            return Ok(new
            {
                g.Status,
                g.RoundNumber,
                g.CurrentTurn,
                boneyardCount = boneyard.Count
            });
        }

        [HttpPost("games/{id:guid}/play")]
        [Authorize]
        public async Task<IActionResult> PlayTile(Guid id, [FromBody] PlayTileDto dto)
        {
            var g = await _db.Games
                .Include(x => x.Participants).ThenInclude(p => p.User)
                .Include(x => x.Board.Where(b => b.RoundNumber == _db.Games.First(ga => ga.Id == id).RoundNumber))
                .Include(x => x.Moves)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (g is null) return NotFound();
            if (g.Status != "active") return BadRequest(new { message = "Round is not active" });

            var user = await _users.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var gp = g.Participants.FirstOrDefault(p => p.UserId == user.Id);
            if (gp is null) return Forbid();

            if (g.CurrentTurn != gp.Position)
                return BadRequest(new { message = "Not your turn" });

            var hand = Mode101GameService.ParseTiles(gp.HandJson);
            var tileInHand = hand.FirstOrDefault(t =>
                (t[0] == dto.TileLeft && t[1] == dto.TileRight) ||
                (t[0] == dto.TileRight && t[1] == dto.TileLeft));

            if (tileInHand is null)
                return BadRequest(new { message = "Tile not in your hand" });

            var side = dto.Side?.ToLowerInvariant() ?? "right";
            if (side != "left" && side != "right")
                return BadRequest(new { message = "Side must be 'left' or 'right'" });

            var currentRoundBoard = g.Board.Where(b => b.RoundNumber == g.RoundNumber).ToList();
            var isFirstTile = currentRoundBoard.Count == 0;
            var isFirstRound = g.RoundNumber == 1;
            var participants = g.Participants.ToList();

            if (isFirstTile && isFirstRound)
            {
                var requiredTile = _gameService.GetRequiredFirstTile(participants);
                if (tileInHand[0] != requiredTile[0] || tileInHand[1] != requiredTile[1])
                {
                    return BadRequest(new { message = $"First round must start with [{requiredTile[0]}|{requiredTile[1]}]" });
                }
            }

            var validation = _gameService.ValidatePlay(
                g.BoardLeft, g.BoardRight,
                tileInHand[0], tileInHand[1],
                side, isFirstTile, isFirstRound);

            if (!validation.IsValid)
                return BadRequest(new { message = validation.Error });

            hand.Remove(tileInHand);
            gp.HandJson = Mode101GameService.SerializeTiles(hand);

            var boardTile = new BoardTile
            {
                GameId = g.Id,
                Index = currentRoundBoard.Count,
                Left = validation.OrientedLeft,
                Right = validation.OrientedRight,
                Side = side,
                IsFlipped = validation.IsFlipped,
                PlayedById = user.Id,
                PlayedByPosition = gp.Position,
                PlayedByColor = gp.Color,
                RoundNumber = g.RoundNumber
            };
            _db.BoardTiles.Add(boardTile);

            if (isFirstTile)
            {
                g.BoardLeft = validation.OrientedLeft;
                g.BoardRight = validation.OrientedRight;
            }
            if (g.IsTeamGame && isFirstTile)
            {
                var team1Players = participants.Where(p => p.Team == 1).ToList();
                var team2Players = participants.Where(p => p.Team == 2).ToList();

                bool team1Blocked = team1Players.All(p =>
                {
                    var h = Mode101GameService.ParseTiles(p.HandJson);
                    return !_gameService.CanPlayerPlay(h, g.BoardLeft, g.BoardRight);
                });

                bool team2Blocked = team2Players.All(p =>
                {
                    var h = Mode101GameService.ParseTiles(p.HandJson);
                    return !_gameService.CanPlayerPlay(h, g.BoardLeft, g.BoardRight);
                });

                if (team1Blocked || team2Blocked)
                {
                    _db.BoardTiles.Remove(boardTile);
                    hand.Add(tileInHand);
                    gp.HandJson = Mode101GameService.SerializeTiles(hand);

                    const int maxAttempts = 10;
                    for (int attempt = 0; attempt < maxAttempts; attempt++)
                    {
                        var newTiles = _gameService.GenerateFullSet();
                        _gameService.ShuffleTiles(newTiles);

                        var idx = 0;
                        bool anyBadHand = false;

                        foreach (var p in participants)
                        {
                            var newHand = newTiles.Skip(idx).Take(7).ToList();
                            idx += 7;
                            p.HandJson = Mode101GameService.SerializeTiles(newHand);

                            if (_gameService.ShouldReshuffleHand(newHand))
                                anyBadHand = true;
                        }

                        if (!anyBadHand)
                        {
                            g.BoneyardJson = Mode101GameService.SerializeTiles(newTiles.Skip(idx).ToList());
                            break;
                        }

                        if (attempt == maxAttempts - 1)
                            g.BoneyardJson = Mode101GameService.SerializeTiles(newTiles.Skip(idx).ToList());
                    }

                    g.BoardLeft = null;
                    g.BoardRight = null;
                    g.CurrentTurn = _gameService.FindFirstRoundStarter(participants);

                    await _db.SaveChangesAsync();

                    await _hub.Clients.Group(g.Id.ToString()).SendAsync("game:update", new
                    {
                        type = "reshuffle",
                        reason = $"Team {(team1Blocked ? 1 : 2)} completely blocked after first tile"
                    });

                    return Ok(new { reshuffled = true, reason = "Team blocked - reshuffled" });
                }
            }
            else if (side == "left")
            {
                g.BoardLeft = validation.NewBoardEnd;
            }
            else
            {
                g.BoardRight = validation.NewBoardEnd;
            }

            var move = new GameMove
            {
                GameId = g.Id,
                PlayerId = user.Id,
                MoveIndex = g.Moves.Count,
                RoundNumber = g.RoundNumber,
                MoveType = "play",
                TileLeft = tileInHand[0],
                TileRight = tileInHand[1],
                PlayedSide = side
            };
            _db.GameMoves.Add(move);

            string? roundOutcome = null;
            string? roundReason = null;
            Guid? roundWinnerId = null;
            int? roundWinningTeam = null;
            int pointsWon = 0;

            if (hand.Count == 0)
            {
                var result = _gameService.DetermineRoundWinner(g, participants, null);
                roundOutcome = result.Reason;
                roundWinnerId = result.WinnerId;
                roundWinningTeam = result.WinningTeam;
                pointsWon = result.PointsWon;
                roundReason = $"Round {g.RoundNumber}: {gp.User.DisplayName} domino - {pointsWon} pts";

                ApplyRoundResult(g, participants, result);
            }
            else
            {
                g.CurrentTurn = (g.CurrentTurn + 1) % participants.Count;

                if (_gameService.IsGameBlocked(g, participants))
                {
                    var result = _gameService.DetermineRoundWinner(g, participants, user.Id);
                    roundOutcome = result.Reason;
                    roundWinnerId = result.WinnerId;
                    roundWinningTeam = result.WinningTeam;
                    pointsWon = result.PointsWon;
                    roundReason = $"Round {g.RoundNumber}: blocked - {pointsWon} pts";

                    ApplyRoundResult(g, participants, result);
                }
            }

            await _db.SaveChangesAsync();

            await _hub.Clients.Group(g.Id.ToString()).SendAsync("game:update", new
            {
                type = "play",
                gameId = g.Id,
                playerId = user.Id,
                playerName = gp.User.DisplayName,
                playerColor = gp.Color,
                tile = new { left = tileInHand[0], right = tileInHand[1] },
                side,
                boardLeft = g.BoardLeft,
                boardRight = g.BoardRight,
                currentTurn = g.CurrentTurn,
                roundOutcome,
                roundReason,
                roundWinnerId,
                roundWinningTeam,
                pointsWon,
                team1Score = g.Team1Score,
                team2Score = g.Team2Score,
                status = g.Status
            });

            return Ok(new
            {
                g.BoardLeft,
                g.BoardRight,
                g.CurrentTurn,
                roundOutcome,
                roundReason,
                pointsWon,
                g.Team1Score,
                g.Team2Score,
                g.Status
            });
        }

        [HttpPost("games/{id:guid}/draw")]
        [Authorize]
        public async Task<IActionResult> DrawTiles(Guid id)
        {
            var g = await _db.Games
                .Include(x => x.Participants).ThenInclude(p => p.User)
                .Include(x => x.Board.Where(b => b.RoundNumber == _db.Games.First(ga => ga.Id == id).RoundNumber))
                .Include(x => x.Moves)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (g is null) return NotFound();
            if (g.Status != "active") return BadRequest(new { message = "Round is not active" });

            var user = await _users.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var gp = g.Participants.FirstOrDefault(p => p.UserId == user.Id);
            if (gp is null) return Forbid();

            if (g.CurrentTurn != gp.Position)
                return BadRequest(new { message = "Not your turn" });

            var hand = Mode101GameService.ParseTiles(gp.HandJson);
            if (_gameService.CanPlayerPlay(hand, g.BoardLeft, g.BoardRight))
                return BadRequest(new { message = "You have a playable tile" });

            var boneyard = Mode101GameService.ParseTiles(g.BoneyardJson);
            var playerCount = g.Participants.Count;
            var playableCount = _gameService.GetPlayableBoneyardCount(boneyard, playerCount);

            var participants = g.Participants.ToList();

            if (playableCount == 0)
            {
                g.CurrentTurn = (g.CurrentTurn + 1) % g.Participants.Count;

                string? roundOutcome = null;
                string? roundReason = null;
                Guid? roundWinnerId = null;
                int? roundWinningTeam = null;
                int pointsWon = 0;

                if (_gameService.IsGameBlocked(g, participants))
                {
                    var result = _gameService.DetermineRoundWinner(g, participants, null);
                    roundOutcome = result.Reason;
                    roundWinnerId = result.WinnerId;
                    roundWinningTeam = result.WinningTeam;
                    pointsWon = result.PointsWon;
                    roundReason = $"Round {g.RoundNumber}: blocked - {pointsWon} pts";
                    ApplyRoundResult(g, participants, result);
                }

                await _db.SaveChangesAsync();

                await _hub.Clients.Group(g.Id.ToString()).SendAsync("game:update", new
                {
                    type = "pass",
                    gameId = g.Id,
                    playerId = user.Id,
                    playerName = gp.User.DisplayName,
                    currentTurn = g.CurrentTurn,
                    roundOutcome,
                    roundReason,
                    roundWinnerId,
                    roundWinningTeam,
                    pointsWon,
                    status = g.Status
                });

                return Ok(new
                {
                    passed = true,
                    currentTurn = g.CurrentTurn,
                    roundOutcome,
                    roundReason,
                    pointsWon,
                    g.Status
                });
            }

            var drawCount = Math.Min(_gameService.GetDrawCount(playerCount), playableCount);
            var drawnTiles = boneyard.Take(drawCount).ToList();
            boneyard = boneyard.Skip(drawCount).ToList();

            hand.AddRange(drawnTiles);
            gp.HandJson = Mode101GameService.SerializeTiles(hand);
            g.BoneyardJson = Mode101GameService.SerializeTiles(boneyard);

            var drawMove = new GameMove
            {
                GameId = g.Id,
                PlayerId = user.Id,
                MoveIndex = g.Moves.Count,
                RoundNumber = g.RoundNumber,
                MoveType = "draw",
                DrawnTilesJson = JsonSerializer.Serialize(drawnTiles)
            };
            _db.GameMoves.Add(drawMove);

            string? drawRoundOutcome = null;
            string? drawRoundReason = null;
            Guid? drawRoundWinnerId = null;
            int? drawRoundWinningTeam = null;
            int drawPointsWon = 0;

            if (_gameService.IsGameBlocked(g, participants))
            {
                var result = _gameService.DetermineRoundWinner(g, participants, null);
                drawRoundOutcome = result.Reason;
                drawRoundWinnerId = result.WinnerId;
                drawRoundWinningTeam = result.WinningTeam;
                drawPointsWon = result.PointsWon;
                drawRoundReason = $"Round {g.RoundNumber}: blocked - {drawPointsWon} pts";

                ApplyRoundResult(g, participants, result);
            }

            await _db.SaveChangesAsync();

            await _hub.Clients.Group(g.Id.ToString()).SendAsync("game:update", new
            {
                type = "draw",
                gameId = g.Id,
                playerId = user.Id,
                playerName = gp.User.DisplayName,
                drawnCount = drawnTiles.Count,
                boneyardCount = boneyard.Count,
                roundOutcome = drawRoundOutcome,
                roundReason = drawRoundReason,
                roundWinnerId = drawRoundWinnerId,
                roundWinningTeam = drawRoundWinningTeam,
                pointsWon = drawPointsWon,
                status = g.Status
            });

            await _hub.Clients.User(user.Id.ToString()).SendAsync("game:private", new
            {
                type = "drew",
                tiles = drawnTiles
            });

            return Ok(new
            {
                drawnTiles,
                boneyardCount = boneyard.Count,
                roundOutcome = drawRoundOutcome,
                roundReason = drawRoundReason,
                pointsWon = drawPointsWon,
                g.Status
            });
        }

        [HttpPost("games/{id:guid}/leave")]
        [Authorize]
        public async Task<IActionResult> LeaveGame(Guid id)
        {
            var g = await _db.Games
                .Include(x => x.Participants)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (g is null) return NotFound();

            var user = await _users.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var gp = g.Participants.FirstOrDefault(p => p.UserId == user.Id);
            if (gp is null) return BadRequest(new { message = "Not in this game" });

            if (g.Status != "waiting" && g.Status != "voting")
                return BadRequest(new { message = "Cannot leave after game started" });

            if (gp.HasVotedToStart)
                return BadRequest(new { message = "Cannot leave after voting to start" });

            _db.GameParticipants.Remove(gp);

            var remaining = g.Participants.Where(p => p.UserId != user.Id).OrderBy(p => p.Position).ToList();
            for (int i = 0; i < remaining.Count; i++)
                remaining[i].Position = i;

            if (remaining.Count == 0)
            {
                _db.Games.Remove(g);
            }
            else if (g.Status == "voting")
            {
                g.Status = "waiting";
                foreach (var p in remaining)
                    p.HasVotedToStart = false;
            }

            await _db.SaveChangesAsync();

            await _hub.Clients.Group(g.Id.ToString()).SendAsync("game:update", new
            {
                type = "player_left",
                playerId = user.Id,
                playerName = user.DisplayName
            });

            return Ok(new { left = true });
        }

        [HttpPost("games/{id:guid}/connect")]
        [AllowAnonymous]
        public IActionResult GetHubRoute(Guid id)
        {
            return Ok(new { hub = "/hubs/game", gameId = id.ToString() });
        }

        [HttpPost("games/{id:guid}/vote-end")]
        [Authorize]
        public async Task<IActionResult> VoteToEnd(Guid id)
        {
            var g = await _db.Games
                .Include(x => x.Participants).ThenInclude(p => p.User)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (g is null) return NotFound();
            if (g.Status == "finished") return BadRequest(new { message = "Game already finished" });

            var user = await _users.GetUserAsync(User);
            if (user is null) return Unauthorized();

            if (!g.Participants.Any(p => p.UserId == user.Id))
                return Forbid();

            var votes = Mode101GameService.ParseVotes(g.VotesToEndJson);

            if (votes.Contains(user.Id))
                return BadRequest(new { message = "Already voted to end" });

            votes.Add(user.Id);
            g.VotesToEndJson = Mode101GameService.SerializeVotes(votes);

            if (votes.Count >= g.Participants.Count)
            {
                g.Status = "finished";
                g.Outcome = "cancelled";
                g.Reason = "All players voted to end";
                g.EndedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync();

                await _hub.Clients.Group(g.Id.ToString()).SendAsync("game:update", new
                {
                    type = "game_cancelled",
                    reason = "Unanimous vote to end"
                });

                return Ok(new { ended = true });
            }

            await _db.SaveChangesAsync();

            await _hub.Clients.Group(g.Id.ToString()).SendAsync("game:update", new
            {
                type = "vote_end",
                playerId = user.Id,
                votesCount = votes.Count,
                required = g.Participants.Count
            });

            return Ok(new { voted = true, votesCount = votes.Count, required = g.Participants.Count });
        }

        private void ApplyRoundResult(Game g, List<GameParticipant> participants, RoundResult result)
        {
            const int lowWinThreshold = 13;
            const int consecutiveResetCount = 3;

            if (result.IsTie)
            {
                g.DeferredPoints += result.TiedSum;
                g.RoundStarterId = result.NextRoundStarterId;

                var roundHistoryEntry = new
                {
                    roundNumber = g.RoundNumber,
                    winnerId = (Guid?)null,
                    winningTeam = (int?)null,
                    pointsWon = 0,
                    reason = "tie",
                    tiedSum = result.TiedSum,
                    deferredTotal = g.DeferredPoints
                };

                AppendRoundHistory(g, roundHistoryEntry);
                g.Status = "round_end";
                return;
            }

            int totalPoints = result.PointsWon;
            bool hasDeferredPoints = g.DeferredPoints > 0;

            if (hasDeferredPoints)
            {
                if (result.PointsWon > 0)
                {
                    totalPoints = (g.DeferredPoints * 2) + result.PointsWon;
                }
                else
                {
                    totalPoints = result.PointsWon;
                }
                g.DeferredPoints = 0;
            }

            if (g.IsTeamGame && result.WinningTeam.HasValue)
            {
                if (result.WinningTeam == 1)
                    g.Team1Score += totalPoints;
                else
                    g.Team2Score += totalPoints;

                var losingTeam = result.WinningTeam == 1 ? 2 : 1;
                var isLowWin = result.PointsWon < lowWinThreshold && result.PointsWon > 0;

                foreach (var p in participants.Where(p => p.Team == losingTeam))
                {
                    p.ConsecutiveLowWins = isLowWin ? p.ConsecutiveLowWins + 1 : 0;

                    if (p.ConsecutiveLowWins >= consecutiveResetCount)
                    {
                        if (losingTeam == 1) g.Team1Score = 0;
                        else g.Team2Score = 0;
                        p.ConsecutiveLowWins = 0;
                    }
                }

                foreach (var p in participants.Where(p => p.Team == result.WinningTeam))
                {
                    p.ConsecutiveLowWins = 0;
                }
            }
            else if (result.WinnerId.HasValue)
            {
                var isLowWin = result.PointsWon < lowWinThreshold && result.PointsWon > 0;

                foreach (var p in participants)
                {
                    if (p.UserId == result.WinnerId)
                    {
                        p.TotalScore += totalPoints;
                        p.RoundScore = totalPoints;
                        p.ConsecutiveLowWins = 0;
                    }
                    else
                    {
                        p.RoundScore = 0;
                        p.ConsecutiveLowWins = isLowWin ? p.ConsecutiveLowWins + 1 : 0;

                        if (p.ConsecutiveLowWins >= consecutiveResetCount)
                        {
                            p.TotalScore = 0;
                            p.ConsecutiveLowWins = 0;
                        }
                    }
                }
            }

            var historyEntry = new
            {
                roundNumber = g.RoundNumber,
                winnerId = result.WinnerId,
                winningTeam = result.WinningTeam,
                pointsWon = result.PointsWon,
                totalAwarded = totalPoints,
                hadDeferred = hasDeferredPoints,
                reason = result.Reason
            };

            AppendRoundHistory(g, historyEntry);

            g.RoundStarterId = result.NextRoundStarterId;

            var isMatchOver = _gameService.CheckMatchWin(g.Team1Score, g.Team2Score, participants, g.IsTeamGame);

            if (isMatchOver)
            {
                g.Status = "finished";
                g.EndedAt = DateTime.UtcNow;

                if (g.IsTeamGame)
                {
                    g.WinningTeam = g.Team1Score >= 101 ? 1 : 2;
                    g.WinnerId = participants.FirstOrDefault(p => p.Team == g.WinningTeam)?.UserId;
                    g.Outcome = "win";
                    g.Reason = $"Team {g.WinningTeam} wins with {(g.WinningTeam == 1 ? g.Team1Score : g.Team2Score)} points";
                }
                else
                {
                    var winner = participants.FirstOrDefault(p => p.TotalScore >= 101);
                    if (winner != null)
                    {
                        g.WinnerId = winner.UserId;
                        g.Outcome = "win";
                        g.Reason = $"{winner.User.DisplayName} wins with {winner.TotalScore} points";
                    }
                }
            }
            else
            {
                g.Status = "round_end";
            }
        }

        private static void AppendRoundHistory(Game g, object entry)
        {
            var history = string.IsNullOrEmpty(g.RoundHistoryJson)
                ? new List<object>()
                : JsonSerializer.Deserialize<List<object>>(g.RoundHistoryJson) ?? new List<object>();
            history.Add(entry);
            g.RoundHistoryJson = JsonSerializer.Serialize(history);
        }
        private static string ShortCode()
            => Convert.ToBase64String(Guid.NewGuid().ToByteArray())
               .Replace("+", "").Replace("/", "").Replace("=", "")[..6].ToUpper();

        public sealed record CreateGameDto(int? MaxPlayers, int? MinPlayers, bool? IsTeamGame, bool? IsAnonymous);
        public sealed record PlayTileDto(int TileLeft, int TileRight, string? Side);
    }
}