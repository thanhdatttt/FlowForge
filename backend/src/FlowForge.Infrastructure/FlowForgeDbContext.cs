using Microsoft.EntityFrameworkCore;

namespace FlowForge.Infrastructure;

public sealed class FlowForgeDbContext(DbContextOptions<FlowForgeDbContext> options)
    : DbContext(options);
