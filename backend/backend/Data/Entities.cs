using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace backend.Data
{
    public sealed class ApplicationUser : IdentityUser<Guid>
    {
        [MaxLength(128)]
        public string DisplayName { get; set; } = string.Empty;
        public ICollection<GameParticipant> Participations { get; set; } = new List<GameParticipant>();
    }

    public sealed class ApplicationRole : IdentityRole<Guid> { }

    public sealed class ApplicationUserRole : IdentityUserRole<Guid>
    {
        public ApplicationUser User { get; set; } = null!;
        public ApplicationRole Role { get; set; } = null!;
    }

    public sealed class Game
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [MaxLength(16)]
        public string Code { get; set; } = string.Empty;

        [MaxLength(32)]
        public string Mode { get; set; } = "mode101";

        [MaxLength(16)]
        public string Status { get; set; } = "waiting";

        public int MaxPlayers { get; set; } = 4;
        public int MinPlayers { get; set; } = 2;

        public int CurrentTurn { get; set; } = 0;
        public int? BoardLeft { get; set; }
        public int? BoardRight { get; set; }

        public string? Outcome { get; set; }
        public string? Reason { get; set; }
        public Guid? WinnerId { get; set; }
        public int? WinningTeam { get; set; }

        public string BoneyardJson { get; set; } = "[]";

        public int RoundNumber { get; set; } = 0;
        public Guid? RoundStarterId { get; set; }
        public string RoundHistoryJson { get; set; } = "[]";

        public bool IsTeamGame { get; set; } = false;
        public int Team1Score { get; set; } = 0;
        public int Team2Score { get; set; } = 0;

        public int DeferredPoints { get; set; } = 0;
        public Guid? DeferredFromTieRoundWinnerId { get; set; }
        public int? DeferredFromTieRoundWinningTeam { get; set; }

        public string VotesJson { get; set; } = "[]";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }

        public ICollection<GameParticipant> Participants { get; set; } = new List<GameParticipant>();
        public ICollection<BoardTile> Board { get; set; } = new List<BoardTile>();
        public ICollection<GameMove> Moves { get; set; } = new List<GameMove>();

        public string? VotesToEndJson { get; set; }

        public bool IsAnonymous { get; set; }

        public string? TelephoneBoardJson { get; set; }
    }

    public sealed class GameParticipant
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid GameId { get; set; }
        public Game Game { get; set; } = null!;

        public Guid UserId { get; set; }
        public ApplicationUser User { get; set; } = null!;

        public int Position { get; set; }
        public int Team { get; set; } = 0;

        [MaxLength(16)]
        public string Color { get; set; } = string.Empty;

        public string HandJson { get; set; } = "[]";

        public int TotalScore { get; set; } = 0;
        public int RoundScore { get; set; } = 0;
        public int ConsecutiveLowWins { get; set; } = 0;

        public bool HasVotedToStart { get; set; } = false;

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    }

    public sealed class BoardTile
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid GameId { get; set; }
        public Game Game { get; set; } = null!;

        public int Index { get; set; }
        public int Left { get; set; }
        public int Right { get; set; }

        [MaxLength(8)]
        public string Side { get; set; } = "right";

        public bool IsFlipped { get; set; } = false;

        public Guid? PlayedById { get; set; }
        public int? PlayedByPosition { get; set; }

        [MaxLength(16)]
        public string? PlayedByColor { get; set; }

        public int RoundNumber { get; set; } = 1;

        public DateTime PlayedAt { get; set; } = DateTime.UtcNow;
    }

    public sealed class GameMove
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid GameId { get; set; }
        public Game Game { get; set; } = null!;

        public Guid PlayerId { get; set; }
        public ApplicationUser Player { get; set; } = null!;

        public int MoveIndex { get; set; }
        public int RoundNumber { get; set; } = 1;

        [MaxLength(16)]
        public string MoveType { get; set; } = "play";

        public int? TileLeft { get; set; }
        public int? TileRight { get; set; }

        [MaxLength(8)]
        public string? PlayedSide { get; set; }

        public string? DrawnTilesJson { get; set; }

        public int? PointsGained { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public sealed class DirectThread
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(64)]
        public string? ThreadKeyId { get; set; }

        public ICollection<DirectThreadMember> Members { get; set; } = new List<DirectThreadMember>();
        public ICollection<DirectMessage> Messages { get; set; } = new List<DirectMessage>();
    }

    public sealed class DirectThreadMember
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ThreadId { get; set; }
        public DirectThread Thread { get; set; } = null!;

        public Guid UserId { get; set; }
        public ApplicationUser User { get; set; } = null!;

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    }

    public sealed class DirectMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ThreadId { get; set; }
        public DirectThread Thread { get; set; } = null!;

        public Guid SenderId { get; set; }
        public ApplicationUser Sender { get; set; } = null!;

        [MaxLength(64)] public string? KeyId { get; set; }
        [MaxLength(64)] public string? NonceB64 { get; set; }
        [MaxLength(64)] public string? MacB64 { get; set; }

        public string CiphertextB64 { get; set; } = string.Empty;

        [MaxLength(64)] public string BodyHashHex { get; set; } = string.Empty;

        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        public DateTime? DeliveredAt { get; set; }
        public DateTime? ReadAt { get; set; }
    }

    public sealed class AppDb : IdentityDbContext<ApplicationUser, ApplicationRole, Guid,
        IdentityUserClaim<Guid>, ApplicationUserRole, IdentityUserLogin<Guid>,
        IdentityRoleClaim<Guid>, IdentityUserToken<Guid>>
    {
        public AppDb(DbContextOptions<AppDb> options) : base(options) { }

        public DbSet<Game> Games => Set<Game>();
        public DbSet<GameParticipant> GameParticipants => Set<GameParticipant>();
        public DbSet<BoardTile> BoardTiles => Set<BoardTile>();
        public DbSet<GameMove> GameMoves => Set<GameMove>();

        public DbSet<DirectThread> DirectThreads => Set<DirectThread>();
        public DbSet<DirectThreadMember> DirectThreadMembers => Set<DirectThreadMember>();
        public DbSet<DirectMessage> DirectMessages => Set<DirectMessage>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);

            b.Entity<ApplicationUser>(e =>
            {
                e.Property(x => x.DisplayName).HasMaxLength(128);
                e.Property(x => x.UserName).HasMaxLength(64).IsRequired();
                e.HasIndex(x => x.UserName).IsUnique();
            });

            b.Entity<ApplicationRole>(e =>
            {
                e.Property(x => x.Name).HasMaxLength(256);
                e.Property(x => x.NormalizedName).HasMaxLength(256);
            });

            b.Entity<ApplicationUserRole>(e =>
            {
                e.HasKey(x => new { x.UserId, x.RoleId });
                e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            });

            b.Entity<Game>(e =>
            {
                e.HasIndex(x => x.Code).IsUnique();
                e.Property(x => x.Code).HasMaxLength(16);
                e.Property(x => x.Mode).HasMaxLength(32);
                e.Property(x => x.Status).HasMaxLength(16);
                e.Property(x => x.BoneyardJson).HasColumnType("nvarchar(max)");
                e.Property(x => x.RoundHistoryJson).HasColumnType("nvarchar(max)");
                e.Property(x => x.VotesJson).HasColumnType("nvarchar(max)");
                e.Property(x => x.TelephoneBoardJson).HasColumnType("nvarchar(max)");
            });

            b.Entity<GameParticipant>(e =>
            {
                e.HasIndex(x => new { x.GameId, x.Position }).IsUnique();
                e.Property(x => x.HandJson).HasColumnType("nvarchar(max)");
                e.Property(x => x.Color).HasMaxLength(16);
                e.HasOne(x => x.Game).WithMany(g => g.Participants)
                    .HasForeignKey(x => x.GameId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.User).WithMany(u => u.Participations)
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            b.Entity<BoardTile>(e =>
            {
                e.HasIndex(x => new { x.GameId, x.RoundNumber, x.Index }).IsUnique();
                e.Property(x => x.Side).HasMaxLength(8);
                e.Property(x => x.PlayedByColor).HasMaxLength(16);
                e.HasOne(x => x.Game).WithMany(g => g.Board)
                    .HasForeignKey(x => x.GameId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            b.Entity<GameMove>(e =>
            {
                e.HasIndex(x => new { x.GameId, x.MoveIndex }).IsUnique();
                e.Property(x => x.MoveType).HasMaxLength(16);
                e.Property(x => x.PlayedSide).HasMaxLength(8);
                e.Property(x => x.DrawnTilesJson).HasColumnType("nvarchar(max)");
                e.HasOne(x => x.Game).WithMany(g => g.Moves)
                    .HasForeignKey(x => x.GameId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            b.Entity<DirectThread>(e =>
            {
                e.Property(x => x.ThreadKeyId).HasMaxLength(64);
                e.HasMany(t => t.Members).WithOne(m => m.Thread)
                    .HasForeignKey(m => m.ThreadId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasMany(t => t.Messages).WithOne(m => m.Thread)
                    .HasForeignKey(m => m.ThreadId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            b.Entity<DirectThreadMember>(e =>
            {
                e.HasIndex(m => new { m.ThreadId, m.UserId }).IsUnique();
            });

            b.Entity<DirectMessage>(e =>
            {
                e.Property(m => m.KeyId).HasMaxLength(64);
                e.Property(m => m.NonceB64).HasMaxLength(64);
                e.Property(m => m.MacB64).HasMaxLength(64);
                e.Property(m => m.CiphertextB64).HasColumnType("nvarchar(max)");
                e.Property(m => m.BodyHashHex).HasMaxLength(64);
                e.HasIndex(m => m.BodyHashHex);
                e.HasIndex(m => m.ThreadId);
                e.HasIndex(m => m.SenderId);
            });

            var adminRoleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var playerRoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            b.Entity<ApplicationRole>().HasData(
                new ApplicationRole { Id = adminRoleId, Name = "Admin", NormalizedName = "ADMIN" },
                new ApplicationRole { Id = playerRoleId, Name = "Player", NormalizedName = "PLAYER" }
            );
        }
    }
}