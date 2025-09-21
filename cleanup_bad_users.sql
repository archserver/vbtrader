-- Clean up users with corrupted password hashes
-- Keep only the working user (hashtest) and remove the broken ones

-- First, let's see all users
SELECT "UserId", "Username", "Email", "CreatedAt", "IsActive" FROM users ORDER BY "UserId";

-- Remove the bad users (bchase, testuser, test123)
-- Note: This will also remove their credentials and related data due to foreign key constraints
DELETE FROM users WHERE "Username" IN ('bchase', 'testuser', 'test123');

-- Verify cleanup
SELECT "UserId", "Username", "Email", "CreatedAt", "IsActive" FROM users ORDER BY "UserId";

-- Optional: Reset the user ID sequence to start from 1 again
-- ALTER SEQUENCE users_userid_seq RESTART WITH 2;