-- Connect to PostgreSQL as a superuser (postgres) and run these commands:
-- psql -U postgres -d vbtrader

-- Grant schema permissions to vbtrader_user
GRANT ALL ON SCHEMA public TO vbtrader_user;
GRANT CREATE ON SCHEMA public TO vbtrader_user;

-- Grant permissions on existing and future tables
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO vbtrader_user;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO vbtrader_user;

-- Grant permissions on future tables and sequences
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO vbtrader_user;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON SEQUENCES TO vbtrader_user;

-- Verify permissions
\dp public.*

-- Exit psql with \q