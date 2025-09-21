did not find the connection string settings location in the files, # VBTrader PostgreSQL Setup Guide

This guide will help you set up PostgreSQL for the VBTrader application using your existing PostgreSQL installation.

## Database Setup

### 1. Create Database and User

Connect to your PostgreSQL server and run these commands:

```sql
-- Create database
CREATE DATABASE vbtrader;

-- Create user (optional - you can use your existing user)
CREATE USER vbtrader_user WITH PASSWORD 'your_secure_password';

-- Grant privileges
GRANT ALL PRIVILEGES ON DATABASE vbtrader TO vbtrader_user;
GRANT ALL ON SCHEMA public TO vbtrader_user;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO vbtrader_user;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO vbtrader_user;
```

### 2. Connection String Configuration

Update your application configuration with your PostgreSQL connection details:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=vbtrader;Username=vbtrader_user;Password=your_secure_password;Port=5432"
  }
}
```

## Database Migrations

### 1. Install EF Core Tools (if not already installed)

```bash
dotnet tool install --global dotnet-ef
```

### 2. Apply Migrations

```bash
cd src/VBTrader.Infrastructure
dotnet ef database update
```

This will create all the necessary tables and indexes.

## Database Schema

### Tables Created

1. **stock_quotes** - Real-time stock price data
   - High-frequency inserts (5x/sec pre-market, 20x/sec market hours)
   - Indexed by symbol and timestamp for fast queries

2. **candlestick_data** - OHLCV data with technical indicators
   - Supports multiple timeframes (1min, 5min, 15min, etc.)
   - Includes MACD, RSI, EMA, Bollinger Bands

3. **market_opportunities** - Scored trading opportunities
   - AI-driven opportunity detection and scoring
   - Linked to news sentiment analysis

4. **trading_sessions** - Session tracking and performance metrics
   - Daily session statistics and performance tracking

### Performance Optimizations

#### Indexes
- **Composite indexes** on (symbol, timestamp) for time-series queries
- **Timestamp indexes** for time-range queries
- **Score indexes** for top opportunities queries

#### Partitioning (Recommended for High Volume)
For production with high data volume, consider monthly partitioning:

```sql
-- Example: Create partitioned table for stock_quotes
CREATE TABLE stock_quotes_y2024m01 PARTITION OF stock_quotes
FOR VALUES FROM ('2024-01-01') TO ('2024-02-01');

CREATE TABLE stock_quotes_y2024m02 PARTITION OF stock_quotes
FOR VALUES FROM ('2024-02-01') TO ('2024-03-01');
-- Continue for each month...
```

## Configuration Settings

### Application Settings Example

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=vbtrader;Username=vbtrader_user;Password=your_password;Port=5432"
  },
  "DatabaseSettings": {
    "CommandTimeout": 30,
    "EnableSensitiveDataLogging": false,
    "BatchSize": 1000,
    "PartitioningEnabled": true
  }
}
```

### Market Data Settings

```json
{
  "MarketSettings": {
    "PreMarketUpdateIntervalMs": 200,
    "MarketHoursUpdateIntervalMs": 50,
    "DataRetentionDays": 7,
    "MaxDataRetentionDays": 547,
    "BatchSize": 500
  }
}
```

## Data Retention and Cleanup

### Automatic Cleanup
The application includes automatic data cleanup:
- Runs daily at 2 AM
- Configurable retention period (1 week to 18 months)
- Uses efficient batch deletions

### Manual Cleanup
```sql
-- Delete old stock quotes (older than 30 days)
DELETE FROM stock_quotes WHERE timestamp < NOW() - INTERVAL '30 days';

-- Delete old candlestick data (older than 90 days)
DELETE FROM candlestick_data WHERE timestamp < NOW() - INTERVAL '90 days';

-- Vacuum tables after large deletions
VACUUM ANALYZE stock_quotes;
VACUUM ANALYZE candlestick_data;
```

## Monitoring and Maintenance

### Performance Monitoring

```sql
-- Check table sizes
SELECT
    schemaname,
    tablename,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) as size
FROM pg_tables
WHERE schemaname = 'public'
ORDER BY pg_total_relation_size(schemaname||'.'||tablename) DESC;

-- Check index usage
SELECT
    indexname,
    idx_scan,
    idx_tup_read,
    idx_tup_fetch
FROM pg_stat_user_indexes
WHERE schemaname = 'public';

-- Check query performance
SELECT
    query,
    calls,
    total_time,
    mean_time,
    rows
FROM pg_stat_statements
WHERE query LIKE '%stock_quotes%'
ORDER BY total_time DESC;
```

### Regular Maintenance

```sql
-- Update table statistics (run weekly)
ANALYZE;

-- Reindex tables (run monthly)
REINDEX TABLE stock_quotes;
REINDEX TABLE candlestick_data;

-- Vacuum tables (run weekly)
VACUUM ANALYZE;
```

## Backup and Recovery

### Automated Backup Script

```bash
#!/bin/bash
BACKUP_DIR="/backups/vbtrader"
DATE=$(date +%Y%m%d_%H%M%S)
BACKUP_FILE="$BACKUP_DIR/vbtrader_backup_$DATE.sql"

# Create backup directory if it doesn't exist
mkdir -p $BACKUP_DIR

# Create backup
pg_dump -h localhost -U vbtrader_user -d vbtrader > $BACKUP_FILE

# Compress backup
gzip $BACKUP_FILE

# Keep only last 30 days of backups
find $BACKUP_DIR -name "vbtrader_backup_*.sql.gz" -mtime +30 -delete

echo "Backup completed: $BACKUP_FILE.gz"
```

### Restore from Backup

```bash
# Decompress backup
gunzip vbtrader_backup_20241217_120000.sql.gz

# Drop and recreate database
dropdb -h localhost -U vbtrader_user vbtrader
createdb -h localhost -U vbtrader_user vbtrader

# Restore from backup
psql -h localhost -U vbtrader_user -d vbtrader < vbtrader_backup_20241217_120000.sql
```

## Troubleshooting

### Common Issues

1. **Connection Issues**
   ```bash
   # Check if PostgreSQL is running
   sudo systemctl status postgresql

   # Check listening ports
   netstat -an | grep 5432
   ```

2. **Permission Issues**
   ```sql
   -- Grant additional permissions if needed
   GRANT USAGE ON SCHEMA public TO vbtrader_user;
   GRANT CREATE ON SCHEMA public TO vbtrader_user;
   ```

3. **Performance Issues**
   - Check index usage with queries above
   - Consider partitioning for large tables
   - Adjust PostgreSQL configuration (`shared_buffers`, `work_mem`, etc.)

4. **High Memory Usage**
   - Adjust batch sizes in application settings
   - Configure PostgreSQL memory settings appropriately

### Configuration Tuning

For high-frequency trading data, consider these PostgreSQL settings:

```postgresql.conf
# Memory settings
shared_buffers = 256MB
work_mem = 4MB
maintenance_work_mem = 64MB

# Checkpoint settings
checkpoint_completion_target = 0.9
wal_buffers = 16MB

# Query planner
random_page_cost = 1.1
effective_cache_size = 1GB

# Logging
log_statement = 'mod'  # Log modifications for debugging
log_duration = on
log_min_duration_statement = 1000  # Log queries > 1 second
```

## Testing the Setup

After setup, test the database connection:

```bash
cd src/VBTrader.Infrastructure
dotnet ef database update
```

The application will automatically create test data during startup. Monitor the logs to ensure data is being written and read correctly.