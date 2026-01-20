-- Drop subscription_id column from societes table
DO $$ 
BEGIN
    -- First drop any foreign key constraints on subscription_id
    IF EXISTS (SELECT 1 FROM information_schema.table_constraints 
               WHERE constraint_name LIKE '%subscription%' 
               AND table_name = 'societes') THEN
        EXECUTE (
            SELECT string_agg('ALTER TABLE societes DROP CONSTRAINT IF EXISTS ' || quote_ident(constraint_name), '; ')
            FROM information_schema.table_constraints 
            WHERE constraint_name LIKE '%subscription%' 
            AND table_name = 'societes'
        );
    END IF;
    
    -- Drop the subscription_id column if it exists
    IF EXISTS (SELECT 1 FROM information_schema.columns 
               WHERE table_name = 'societes' AND column_name = 'subscription_id') THEN
        ALTER TABLE societes DROP COLUMN subscription_id;
        RAISE NOTICE 'Dropped subscription_id column from societes';
    ELSE
        RAISE NOTICE 'subscription_id column does not exist';
    END IF;
    
    -- Also drop subscriptions table if it exists
    DROP TABLE IF EXISTS subscriptions CASCADE;
    RAISE NOTICE 'Dropped subscriptions table';
END $$;
