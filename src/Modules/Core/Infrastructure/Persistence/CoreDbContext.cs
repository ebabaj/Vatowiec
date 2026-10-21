using Microsoft.EntityFrameworkCore;

namespace Vatowiec.Core.Infrastructure.Persistence;

// Model i mapowania pojawią się dopiero po uzgodnieniu zakresu danych.
public sealed class CoreDbContext(DbContextOptions<CoreDbContext> options) : DbContext(options);
