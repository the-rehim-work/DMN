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
    [Route("api/telephone")]
    public sealed class TelephoneGamesController : ControllerBase
    {
        private readonly AppDb _db;
        private readonly IHubContext<GameHub> _hub;
        private readonly UserManager<ApplicationUser> _users;
        private readonly ITelephoneGameService _gameService;

        public TelephoneGamesController(
            AppDb db,
            IHubContext<GameHub> hub,
            UserManager<ApplicationUser> users,
            ITelephoneGameService gameService)
        {
            _db = db;
            _hub = hub;
            _users = users;
            _gameService = gameService;
        }

        [HttpPost("games")]
        [Authorize]
        public async Task<IActionResult> CreateGame([FromBody] CreateTelephoneGameDto? dto)
        {
            var maxPlayers = dto?.MaxPlayers ?? 4;
            var minPlayers = dto?.MinPlayers ?? 2;

            if (maxPlayers < 2 || maxPlayers > 4)
                return BadRequest(new { message = "MaxPlayers must be 2-4" });

            var code = ShortCode();
            var game = new Game
            {
                Code = code,
                Mode = "telephone",
                MaxPlayers = maxPlayers,
                MinPlayers = minPlayers,
                IsTeamGame = false,
                Status = "waiting",
                TelephoneBoardJson = "{}"
            };

            _db.Games.Add(game);
            await _db.SaveChangesAsync();

            return Ok(new { game.Id, game.Code, game.Mode, game.Status, game.MaxPlayers });
        }

        [HttpPost("games/{id:guid}/play")]
        [Authorize]
        public async Task<IActionResult> PlayTile(Guid id, [FromBody] TelephonePlayDto dto)
        {
            var g = await _db.Games
                .Include(x => x.Participants).ThenInclude(p => p.User)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (g is null) return NotFound();
            if (g.Mode != "telephone") return BadRequest(new { message = "Not a telephone game" });
            if (g.Status != "active") return BadRequest(new { message = "Round is not active" });

            var user = await _users.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var gp = g.Participants.FirstOrDefault(p => p.UserId == user.Id);
            if (gp is null) return Forbid();

            if (g.CurrentTurn != gp.Position)
                return BadRequest(new { message = "Not your turn" });

            var hand = TelephoneGameService.ParseTiles(gp.HandJson);
            var tileInHand = hand.FirstOrDefault(t =>
                (t[0] == dto.TileLeft && t[1] == dto.TileRight) ||
                (t[0] == dto.TileRight && t[1] == dto.TileLeft));

            if (tileInHand is null)
                return BadRequest(new { message = "Tile not in your hand" });

            var board = TelephoneGameService.ParseBoardState(g.TelephoneBoardJson ?? "{}");
            var isFirstTile = board.Tiles.Count == 0;

            if (isFirstTile && g.RoundNumber == 1)
            {
                var required = _gameService.GetRequiredFirstTile(g.Participants.ToList());
                if (required != null)
                {
                    bool matches = (tileInHand[0] == required[0] && tileInHand[1] == required[1]) ||
                                   (tileInHand[0] == required[1] && tileInHand[1] == required[0]);
                    if (!matches)
                        return BadRequest(new { message = $"Must play [{required[0]}|{required[1]}] first" });
                }
            }

            var result = _gameService.ValidateAndPlay(board, tileInHand[0], tileInHand[1], dto.Side ?? "right", isFirstTile);

            if (!result.IsValid)
                return BadRequest(new { message = result.Error });

            hand.Remove(tileInHand);
            gp.HandJson = TelephoneGameService.SerializeTiles(hand);

            result.Board.Tiles.Last().PlayedById = user.Id;
            result.Board.Tiles.Last().PlayedByPosition = gp.Position;
            result.Board.Tiles.Last().PlayedByColor = gp.Color;

            g.TelephoneBoardJson = TelephoneGameService.SerializeBoardState(result.Board);
            g.BoardLeft = result.Board.LeftEnd;
            g.BoardRight = result.Board.RightEnd;

            if (result.PointsAwarded > 0)
                gp.TotalScore += result.PointsAwarded;

            string? roundOutcome = null;
            int roundPoints = 0;

            if (hand.Count == 0)
            {
                var roundResult = _gameService.DetermineRoundWinner(g, g.Participants.ToList(), null);
                roundOutcome = roundResult.Reason;
                roundPoints = roundResult.PointsWon;

                if (roundResult.WinnerId.HasValue)
                {
                    var winner = g.Participants.First(p => p.UserId == roundResult.WinnerId);
                    winner.TotalScore += roundPoints;
                }

                g.Status = _gameService.CheckMatchWin(g.Participants.ToList()) ? "finished" : "round_end";
                g.RoundStarterId = roundResult.NextRoundStarterId;
            }
            else
            {
                g.CurrentTurn = (g.CurrentTurn + 1) % g.Participants.Count;

                if (_gameService.IsGameBlocked(g, g.Participants.ToList(), result.Board))
                {
                    var roundResult = _gameService.DetermineRoundWinner(g, g.Participants.ToList(), user.Id);
                    roundOutcome = roundResult.Reason;
                    roundPoints = roundResult.PointsWon;

                    if (roundResult.WinnerId.HasValue)
                    {
                        var winner = g.Participants.First(p => p.UserId == roundResult.WinnerId);
                        winner.TotalScore += roundPoints;
                    }

                    g.Status = _gameService.CheckMatchWin(g.Participants.ToList()) ? "finished" : "round_end";
                    g.RoundStarterId = roundResult.NextRoundStarterId;
                }
            }

            await _db.SaveChangesAsync();

            await _hub.Clients.Group(g.Id.ToString()).SendAsync("game:update", new
            {
                type = "play",
                gameId = g.Id,
                playerId = user.Id,
                tile = new { left = tileInHand[0], right = tileInHand[1] },
                side = dto.Side,
                pointsAwarded = result.PointsAwarded,
                boardPoints = _gameService.CalculateBoardPoints(result.Board),
                currentTurn = g.CurrentTurn,
                roundOutcome,
                roundPoints,
                status = g.Status
            });

            return Ok(new
            {
                pointsAwarded = result.PointsAwarded,
                boardPoints = _gameService.CalculateBoardPoints(result.Board),
                currentTurn = g.CurrentTurn,
                roundOutcome,
                roundPoints,
                status = g.Status
            });
        }

        [HttpPost("games/{id:guid}/play-combo")]
        [Authorize]
        public async Task<IActionResult> PlayCombo(Guid id, [FromBody] TelephoneComboDto dto)
        {
            var g = await _db.Games
                .Include(x => x.Participants).ThenInclude(p => p.User)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (g is null) return NotFound();
            if (g.Mode != "telephone") return BadRequest(new { message = "Not a telephone game" });
            if (g.Status != "active") return BadRequest(new { message = "Round is not active" });

            var user = await _users.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var gp = g.Participants.FirstOrDefault(p => p.UserId == user.Id);
            if (gp is null) return Forbid();

            if (g.CurrentTurn != gp.Position)
                return BadRequest(new { message = "Not your turn" });

            var hand = TelephoneGameService.ParseTiles(gp.HandJson);
            var board = TelephoneGameService.ParseBoardState(g.TelephoneBoardJson ?? "{}");

            var tile1 = hand.FirstOrDefault(t => t[0] == dto.Tile1Left && t[1] == dto.Tile1Right);
            var tile2 = hand.FirstOrDefault(t => t[0] == dto.Tile2Left && t[1] == dto.Tile2Right);

            if (tile1 == null || tile2 == null)
                return BadRequest(new { message = "Tiles not in hand" });

            if (tile1[0] != tile1[1] || tile2[0] != tile2[1])
                return BadRequest(new { message = "Both tiles must be doubles for combo" });

            string side1 = tile1[0] == board.LeftEnd ? "left" : tile1[0] == board.RightEnd ? "right" : "";
            if (string.IsNullOrEmpty(side1))
                return BadRequest(new { message = "First double cannot be played" });

            var result1 = _gameService.ValidateAndPlay(board, tile1[0], tile1[1], side1, false);
            if (!result1.IsValid)
                return BadRequest(new { message = result1.Error });

            string side2 = tile2[0] == result1.Board.LeftEnd ? "left" : tile2[0] == result1.Board.RightEnd ? "right" : "";
            if (string.IsNullOrEmpty(side2))
                return BadRequest(new { message = "Second double cannot be played" });

            var result2 = _gameService.ValidateAndPlay(result1.Board, tile2[0], tile2[1], side2, false);
            if (!result2.IsValid)
                return BadRequest(new { message = result2.Error });

            int comboPoints = _gameService.CalculateBoardPoints(result2.Board);
            if (comboPoints % 5 != 0)
                return BadRequest(new { message = "Combo does not result in points divisible by 5" });

            hand.Remove(tile1);
            hand.Remove(tile2);
            gp.HandJson = TelephoneGameService.SerializeTiles(hand);

            int awarded = comboPoints / 5;
            gp.TotalScore += awarded;

            g.TelephoneBoardJson = TelephoneGameService.SerializeBoardState(result2.Board);
            g.BoardLeft = result2.Board.LeftEnd;
            g.BoardRight = result2.Board.RightEnd;
            g.CurrentTurn = (g.CurrentTurn + 1) % g.Participants.Count;

            await _db.SaveChangesAsync();

            await _hub.Clients.Group(g.Id.ToString()).SendAsync("game:update", new
            {
                type = "combo",
                playerId = user.Id,
                tiles = new[] { tile1, tile2 },
                pointsAwarded = awarded,
                currentTurn = g.CurrentTurn
            });

            return Ok(new { pointsAwarded = awarded, currentTurn = g.CurrentTurn });
        }

        [HttpGet("games/{id:guid}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetGame(Guid id)
        {
            var g = await _db.Games
                .Include(x => x.Participants).ThenInclude(p => p.User)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (g is null) return NotFound();
            if (g.Mode != "telephone") return BadRequest(new { message = "Not a telephone game" });

            ApplicationUser? me = null;
            if (User?.Identity?.IsAuthenticated == true)
                me = await _users.GetUserAsync(User);

            var myParticipant = me != null
                ? g.Participants.FirstOrDefault(p => p.UserId == me.Id)
                : null;

            var board = TelephoneGameService.ParseBoardState(g.TelephoneBoardJson ?? "{}");
            var boneyard = TelephoneGameService.ParseTiles(g.BoneyardJson);

            List<int[][]>? comboTiles = null;
            if (myParticipant != null && g.Status == "active" && g.CurrentTurn == myParticipant.Position)
            {
                var hand = TelephoneGameService.ParseTiles(myParticipant.HandJson);
                comboTiles = _gameService.FindComboPlay(hand, board);
            }

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
                g.RoundNumber,
                g.CreatedAt,
                g.StartedAt,
                g.EndedAt,
                BoneyardCount = boneyard.Count,
                PlayableBoneyardCount = _gameService.GetPlayableBoneyardCount(boneyard, g.Participants.Count),
                BoardPoints = _gameService.CalculateBoardPoints(board),
                Board = new
                {
                    board.LeftEnd,
                    board.RightEnd,
                    Tiles = board.Tiles.Select(t => new
                    {
                        t.Left,
                        t.Right,
                        t.Position,
                        t.AttachedToDoubleIndex,
                        t.AttachedSide,
                        t.PlayedById,
                        t.PlayedByPosition,
                        t.PlayedByColor,
                        t.IsFlipped
                    }),
                    Telephones = board.Telephones.Select(tel => new
                    {
                        tel.Value,
                        tel.TileIndex,
                        tel.IsClosed,
                        tel.TopEnd,
                        tel.BottomEnd,
                        tel.HasTop,
                        tel.HasBottom
                    })
                },
                Participants = g.Participants
                    .OrderBy(p => p.Position)
                    .Select(p => new
                    {
                        p.UserId,
                        DisplayName = p.User.DisplayName,
                        p.Position,
                        p.Color,
                        p.TotalScore,
                        p.RoundScore,
                        p.HasVotedToStart,
                        TileCount = TelephoneGameService.ParseTiles(p.HandJson).Count,
                        IsCurrentTurn = g.CurrentTurn == p.Position
                    }),
                MyHand = myParticipant != null
                    ? TelephoneGameService.ParseTiles(myParticipant.HandJson)
                    : new List<int[]>(),
                AllHands = g.Status == "round_end" || g.Status == "finished"
                    ? g.Participants.Select(p => new
                    {
                        p.UserId,
                        p.Position,
                        Hand = TelephoneGameService.ParseTiles(p.HandJson)
                    }).ToList()
                    : null,
                MyPosition = myParticipant?.Position,
                RequiredFirstTile = g.RoundNumber == 1 && g.Status == "active" && board.Tiles.Count == 0
                    ? _gameService.GetRequiredFirstTile(g.Participants.ToList())
                    : null,
                ComboAvailable = comboTiles != null && comboTiles.Count > 0,
                ComboTiles = comboTiles
            };

            return Ok(dto);
        }

        [HttpGet("games")]
        [Authorize]
        public async Task<IActionResult> GetGames(
            [FromQuery] string status = "all",
            [FromQuery] bool onlyMine = false)
        {
            var me = await _users.GetUserAsync(User);

            IQueryable<Game> query = _db.Games
                .Include(x => x.Participants).ThenInclude(p => p.User)
                .Where(x => x.Mode == "telephone");

            if (status is "waiting" or "voting" or "active" or "round_end" or "finished")
                query = query.Where(x => x.Status == status);

            if (onlyMine)
                query = query.Where(x => x.Participants.Any(p => p.UserId == me!.Id));

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
                    x.RoundNumber,
                    x.CreatedAt,
                    Participants = x.Participants
                        .OrderBy(p => p.Position)
                        .Select(p => new
                        {
                            p.UserId,
                            DisplayName = p.User.DisplayName,
                            p.Position,
                            p.Color,
                            p.TotalScore
                        })
                })
                .ToListAsync();

            return Ok(rows);
        }

        [HttpPost("games/{id:guid}/join")]
        [Authorize]
        public async Task<IActionResult> JoinGame(Guid id)
        {
            var g = await _db.Games
                .Include(x => x.Participants)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (g is null) return NotFound();
            if (g.Mode != "telephone") return BadRequest(new { message = "Not a telephone game" });

            var user = await _users.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var existing = g.Participants.FirstOrDefault(p => p.UserId == user.Id);
            if (existing != null)
                return Ok(new { g.Id, g.Code, position = existing.Position, existing.Color, g.Status });

            if (g.Participants.Count >= g.MaxPlayers)
                return BadRequest(new { message = "Game is full" });

            if (g.Status != "waiting")
                return BadRequest(new { message = g.Status == "voting" ? "Cannot join; voting in progress" : "Cannot join; game started" });

            var takenPositions = g.Participants.Select(p => p.Position).ToHashSet();
            int chosenPosition = Enumerable.Range(0, g.MaxPlayers).First(p => !takenPositions.Contains(p));
            var color = _gameService.AssignColor(chosenPosition);

            var gp = new GameParticipant
            {
                GameId = g.Id,
                UserId = user.Id,
                Position = chosenPosition,
                Team = 0,
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
                    color
                }
            });

            return Ok(new { g.Id, g.Code, position = chosenPosition, color, g.Status });
        }

        [HttpPost("games/{id:guid}/vote-start")]
        [Authorize]
        public async Task<IActionResult> VoteToStart(Guid id)
        {
            var g = await _db.Games
                .Include(x => x.Participants)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (g is null) return NotFound();
            if (g.Mode != "telephone") return BadRequest(new { message = "Not a telephone game" });

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

            if (allVoted)
                return await StartRound(id);

            await _hub.Clients.Group(g.Id.ToString()).SendAsync("game:update", new
            {
                type = "vote",
                playerId = user.Id,
                votedCount = g.Participants.Count(p => p.HasVotedToStart),
                totalPlayers = g.Participants.Count,
                allVoted
            });

            return Ok(new { voted = true, allVoted });
        }

        [HttpPost("games/{id:guid}/start-round")]
        [Authorize]
        public async Task<IActionResult> StartRound(Guid id)
        {
            var g = await _db.Games
                .Include(x => x.Participants)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (g is null) return NotFound();
            if (g.Mode != "telephone") return BadRequest(new { message = "Not a telephone game" });

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
            var participants = g.Participants.OrderBy(p => p.Position).ToList();

            const int maxReshuffleAttempts = 10;
            var reshuffleAttempt = 0;
            bool needsReshuffle;
            int tileIndex;

            do
            {
                needsReshuffle = false;
                tileIndex = 0;

                foreach (var p in participants)
                {
                    var hand = tiles.Skip(tileIndex).Take(tilesPerPlayer).ToList();
                    tileIndex += tilesPerPlayer;
                    p.HandJson = TelephoneGameService.SerializeTiles(hand);
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

            int starterIndex = 0;

            if (isFirstRound)
            {
                var required = _gameService.GetRequiredFirstTile(participants);
                if (required == null)
                {
                    tiles = _gameService.GenerateFullSet();
                    _gameService.ShuffleTiles(tiles);
                    tileIndex = 0;
                    foreach (var p in participants)
                    {
                        var hand = tiles.Skip(tileIndex).Take(tilesPerPlayer).ToList();
                        tileIndex += tilesPerPlayer;
                        p.HandJson = TelephoneGameService.SerializeTiles(hand);
                    }
                }
                starterIndex = _gameService.FindFirstRoundStarter(participants);
            }
            else if (g.RoundStarterId.HasValue)
            {
                var starter = participants.FirstOrDefault(p => p.UserId == g.RoundStarterId);
                if (starter != null) starterIndex = starter.Position;
            }

            var boneyard = tiles.Skip(tileIndex).ToList();
            g.BoneyardJson = TelephoneGameService.SerializeTiles(boneyard);
            g.TelephoneBoardJson = "{}";
            g.CurrentTurn = starterIndex;
            g.Status = "active";
            g.RoundNumber++;
            g.BoardLeft = null;
            g.BoardRight = null;

            if (isFirstRound)
                g.StartedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            await _hub.Clients.Group(g.Id.ToString()).SendAsync("game:update", new
            {
                type = "round_start",
                roundNumber = g.RoundNumber,
                currentTurn = g.CurrentTurn,
                boneyardCount = boneyard.Count
            });

            return Ok(new { g.Status, g.RoundNumber, g.CurrentTurn });
        }

        [HttpPost("games/{id:guid}/draw")]
        [Authorize]
        public async Task<IActionResult> DrawTiles(Guid id)
        {
            var g = await _db.Games
                .Include(x => x.Participants).ThenInclude(p => p.User)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (g is null) return NotFound();
            if (g.Mode != "telephone") return BadRequest(new { message = "Not a telephone game" });
            if (g.Status != "active") return BadRequest(new { message = "Round is not active" });

            var user = await _users.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var gp = g.Participants.FirstOrDefault(p => p.UserId == user.Id);
            if (gp is null) return Forbid();

            if (g.CurrentTurn != gp.Position)
                return BadRequest(new { message = "Not your turn" });

            var hand = TelephoneGameService.ParseTiles(gp.HandJson);
            var board = TelephoneGameService.ParseBoardState(g.TelephoneBoardJson ?? "{}");

            if (_gameService.CanPlayerPlay(hand, board))
                return BadRequest(new { message = "You have a playable tile" });

            var boneyard = TelephoneGameService.ParseTiles(g.BoneyardJson);
            var playerCount = g.Participants.Count;
            var playableCount = _gameService.GetPlayableBoneyardCount(boneyard, playerCount);
            var participants = g.Participants.ToList();
            int roundPoints = 0;

            if (playableCount == 0)
            {
                g.CurrentTurn = (g.CurrentTurn + 1) % g.Participants.Count;

                string? roundOutcome = null;

                if (_gameService.IsGameBlocked(g, participants, board))
                {
                    var result = _gameService.DetermineRoundWinner(g, participants, null);
                    roundOutcome = result.Reason;
                    roundPoints = result.PointsWon;

                    if (result.WinnerId.HasValue)
                    {
                        var winner = participants.First(p => p.UserId == result.WinnerId);
                        winner.TotalScore += roundPoints;
                    }

                    g.Status = _gameService.CheckMatchWin(participants) ? "finished" : "round_end";
                    g.RoundStarterId = result.NextRoundStarterId;
                }

                await _db.SaveChangesAsync();

                await _hub.Clients.Group(g.Id.ToString()).SendAsync("game:update", new
                {
                    type = "pass",
                    playerId = user.Id,
                    currentTurn = g.CurrentTurn,
                    roundOutcome,
                    roundPoints,
                    status = g.Status
                });

                return Ok(new { passed = true, currentTurn = g.CurrentTurn, roundOutcome, roundPoints, g.Status });
            }

            var drawCount = Math.Min(_gameService.GetDrawCount(playerCount), playableCount);
            var drawnTiles = boneyard.Take(drawCount).ToList();
            boneyard = boneyard.Skip(drawCount).ToList();

            hand.AddRange(drawnTiles);
            gp.HandJson = TelephoneGameService.SerializeTiles(hand);
            g.BoneyardJson = TelephoneGameService.SerializeTiles(boneyard);

            string? drawRoundOutcome = null;
            int drawRoundPoints = 0;

            if (_gameService.IsGameBlocked(g, participants, board))
            {
                var result = _gameService.DetermineRoundWinner(g, participants, null);
                drawRoundOutcome = result.Reason;
                drawRoundPoints = result.PointsWon;

                if (result.WinnerId.HasValue)
                {
                    var winner = participants.First(p => p.UserId == result.WinnerId);
                    winner.TotalScore += roundPoints;
                }

                g.Status = _gameService.CheckMatchWin(participants) ? "finished" : "round_end";
                g.RoundStarterId = result.NextRoundStarterId;
            }

            await _db.SaveChangesAsync();

            await _hub.Clients.Group(g.Id.ToString()).SendAsync("game:update", new
            {
                type = "draw",
                playerId = user.Id,
                drawnCount = drawnTiles.Count,
                boneyardCount = boneyard.Count,
                roundOutcome = drawRoundOutcome,
                roundPoints = drawRoundPoints,
                status = g.Status
            });

            return Ok(new { drawnTiles, boneyardCount = boneyard.Count, roundOutcome = drawRoundOutcome, g.Status });
        }

        [HttpPost("games/{id:guid}/leave")]
        [Authorize]
        public async Task<IActionResult> LeaveGame(Guid id)
        {
            var g = await _db.Games
                .Include(x => x.Participants).ThenInclude(p => p.User)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (g is null) return NotFound();

            var user = await _users.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var gp = g.Participants.FirstOrDefault(p => p.UserId == user.Id);
            if (gp is null) return BadRequest(new { message = "Not in this game" });

            if (g.Status == "finished")
                return BadRequest(new { message = "Game already finished" });

            if (g.Status == "active" || g.Status == "round_end")
                return BadRequest(new { message = "Cannot leave active game" });

            _db.GameParticipants.Remove(gp);

            var remaining = g.Participants.Where(p => p.UserId != user.Id).OrderBy(p => p.Position).ToList();
            for (int i = 0; i < remaining.Count; i++)
            {
                remaining[i].Position = i;
                remaining[i].Color = _gameService.AssignColor(i);
            }

            if (remaining.Count == 0)
                _db.Games.Remove(g);
            else
            {
                g.Status = "waiting";
                foreach (var p in remaining)
                    p.HasVotedToStart = false;
            }

            await _db.SaveChangesAsync();

            await _hub.Clients.Group(g.Id.ToString()).SendAsync("game:update", new
            {
                type = "player_left",
                playerId = user.Id
            });

            return Ok(new { left = true });
        }

        [HttpPost("games/{id:guid}/vote-end")]
        [Authorize]
        public async Task<IActionResult> VoteToEnd(Guid id)
        {
            var g = await _db.Games
                .Include(x => x.Participants)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (g is null) return NotFound();
            if (g.Status == "finished") return BadRequest(new { message = "Game already finished" });

            var user = await _users.GetUserAsync(User);
            if (user is null) return Unauthorized();

            if (!g.Participants.Any(p => p.UserId == user.Id))
                return Forbid();

            var votes = Mode101GameService.ParseVotes(g.VotesToEndJson);

            if (votes.Contains(user.Id))
                return BadRequest(new { message = "Already voted" });

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

            return Ok(new { voted = true, votesCount = votes.Count, required = g.Participants.Count });
        }

        private static string ShortCode()
            => Convert.ToBase64String(Guid.NewGuid().ToByteArray())
               .Replace("+", "").Replace("/", "").Replace("=", "")[..6].ToUpper();

        public sealed record CreateTelephoneGameDto(int? MaxPlayers, int? MinPlayers);
        public sealed record TelephonePlayDto(int TileLeft, int TileRight, string? Side);
        public sealed record TelephoneComboDto(int Tile1Left, int Tile1Right, int Tile2Left, int Tile2Right);
    }
}