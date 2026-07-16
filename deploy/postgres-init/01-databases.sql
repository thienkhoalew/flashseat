SELECT 'CREATE DATABASE flashseat_identity'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'flashseat_identity')\gexec

SELECT 'CREATE DATABASE flashseat_events'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'flashseat_events')\gexec

SELECT 'CREATE DATABASE flashseat_booking'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'flashseat_booking')\gexec

SELECT 'CREATE DATABASE flashseat_payment'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'flashseat_payment')\gexec
