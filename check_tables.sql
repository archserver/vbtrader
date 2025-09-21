-- Check what tables exist in the database
\dt

-- Alternative way to list tables
SELECT table_name
FROM information_schema.tables
WHERE table_schema = 'public'
  AND table_type = 'BASE TABLE'
ORDER BY table_name;

-- If users table exists, show its structure
\d users

-- Show all users if table exists
SELECT * FROM users;