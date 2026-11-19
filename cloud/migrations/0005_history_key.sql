-- Which personal key made a console change, so the history can tell apart keys of one user
-- (e.g. a key handed to a colleague). NULL for web changes and for console changes made before.
ALTER TABLE device_history ADD COLUMN key_id TEXT;
