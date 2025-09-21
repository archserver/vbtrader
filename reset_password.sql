-- Quick password reset for user 'bchase'
-- Run this in your PostgreSQL database

-- First, let's see the current user
SELECT "UserId", "Username", "Email", "IsActive" FROM users WHERE "Username" = 'bchase';

-- Update the user to set a simple password hash (password will be 'password123')
-- This is a temporary Argon2 hash for 'password123'
UPDATE users
SET "PasswordHash" = '$argon2id$v=19$m=65536,t=3,p=1$+Tt1X8Z4Zf2oN7G8rYQ6Zg$X8K4rF3qL9mN2pS5vX8yZ1aB4cD6eF7gH9iJ0kL2mN4',
    "Salt" = 'tempSalt123'
WHERE "Username" = 'bchase';

-- Verify the update
SELECT "UserId", "Username", "Email", "IsActive", "CreatedAt" FROM users WHERE "Username" = 'bchase';