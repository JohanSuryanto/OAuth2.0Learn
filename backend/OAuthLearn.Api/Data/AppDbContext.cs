using Microsoft.EntityFrameworkCore;

namespace OAuthLearn.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<PasswordCredential> PasswordCredentials => Set<PasswordCredential>();

    public DbSet<OneTimeToken> OneTimeTokens => Set<OneTimeToken>();

    public DbSet<RateLimitEvent> RateLimitEvents => Set<RateLimitEvent>();

    public DbSet<MailboxMessage> MailboxMessages => Set<MailboxMessage>();

    public DbSet<UserSession> UserSessions => Set<UserSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("users");
            user.HasKey(u => u.Id);
            user.Property(u => u.Id).ValueGeneratedNever();
            // Nullable for password-only accounts; Postgres allows many NULLs in a unique index.
            user.Property(u => u.GoogleSubject);
            user.HasIndex(u => u.GoogleSubject).IsUnique();
            user.Property(u => u.Email).IsRequired();
            user.Property(u => u.EmailNormalized).IsRequired();
            user.HasIndex(u => u.EmailNormalized).IsUnique();
            user.Property(u => u.DisplayName);
            user.Property(u => u.CreatedAt).IsRequired().HasColumnType("timestamptz");
            user.Property(u => u.LastLoginAt).IsRequired().HasColumnType("timestamptz");
            user.Property(u => u.EmailVerifiedAt).HasColumnType("timestamptz");
            user.Property(u => u.CreatedVia).IsRequired().HasDefaultValue(UserCreatedVia.Google);
            user.Property(u => u.SessionVersion).IsRequired().HasDefaultValue(0);
            user.HasOne(u => u.PasswordCredential)
                .WithOne()
                .HasForeignKey<PasswordCredential>(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PasswordCredential>(credential =>
        {
            credential.ToTable("password_credentials");
            credential.HasKey(c => c.UserId);
            credential.Property(c => c.ActiveHash);
            credential.Property(c => c.PendingHash);
            credential.Property(c => c.ActiveSetAt).HasColumnType("timestamptz");
            credential.Property(c => c.PendingSetAt).HasColumnType("timestamptz");
        });

        modelBuilder.Entity<OneTimeToken>(token =>
        {
            token.ToTable("one_time_tokens");
            token.HasKey(t => t.Id);
            token.Property(t => t.Id).ValueGeneratedNever();
            token.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
            token.HasIndex(t => t.UserId);
            token.Property(t => t.Purpose).IsRequired();
            token.Property(t => t.TokenHash).IsRequired();
            token.HasIndex(t => t.TokenHash).IsUnique();
            token.Property(t => t.CreatedAt).IsRequired().HasColumnType("timestamptz");
            token.Property(t => t.ExpiresAt).IsRequired().HasColumnType("timestamptz");
            token.Property(t => t.UsedAt).HasColumnType("timestamptz");
        });

        modelBuilder.Entity<RateLimitEvent>(evt =>
        {
            evt.ToTable("rate_limit_events");
            evt.HasKey(e => e.Id);
            evt.Property(e => e.Id).UseIdentityAlwaysColumn();
            evt.Property(e => e.Bucket).IsRequired();
            evt.Property(e => e.KeyHash).IsRequired();
            evt.Property(e => e.OccurredAt).IsRequired().HasColumnType("timestamptz");
            evt.HasIndex(e => new { e.Bucket, e.KeyHash, e.OccurredAt }).IsDescending(false, false, true);
        });

        modelBuilder.Entity<UserSession>(session =>
        {
            session.ToTable("user_sessions");
            session.HasKey(s => s.Id);
            session.Property(s => s.Id).ValueGeneratedNever();
            session.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            session.HasIndex(s => s.UserId);
            session.Property(s => s.CreatedAt).IsRequired().HasColumnType("timestamptz");
            session.Property(s => s.AuthTime).IsRequired().HasColumnType("timestamptz");
            session.Property(s => s.LastAuthAt).IsRequired().HasColumnType("timestamptz");
            session.Property(s => s.LastSeenAt).IsRequired().HasColumnType("timestamptz");
            session.Property(s => s.DeviceLabel).HasMaxLength(200);
            session.Property(s => s.IpMasked);
            session.Property(s => s.RevokedAt).HasColumnType("timestamptz");
        });

        modelBuilder.Entity<MailboxMessage>(message =>
        {
            message.ToTable("mailbox_messages");
            message.HasKey(m => m.Id);
            message.Property(m => m.Id).ValueGeneratedNever();
            message.Property(m => m.ToAddress).IsRequired();
            message.Property(m => m.Subject).IsRequired();
            message.Property(m => m.BodyText).IsRequired();
            message.Property(m => m.CreatedAt).IsRequired().HasColumnType("timestamptz");
        });
    }
}
