#!/bin/bash
set -e

# Выполняем стандартный entrypoint
exec /usr/local/bin/docker-entrypoint.sh postgres &
POSTGRES_PID=$!

# Ждем, пока PostgreSQL полностью запустится
until pg_isready -h localhost -U postgres; do
  echo "Ждем запуска PostgreSQL..."
  sleep 2
done

echo "PostgreSQL запущен, создаем расширение vector..."

# Создаем расширение pgvector
psql -v ON_ERROR_STOP=1 --username postgres --dbname "$POSTGRES_DB" <<-EOSQL
    CREATE EXTENSION IF NOT EXISTS vector;
    SELECT 'Расширение vector успешно создано';
EOSQL

echo "Инициализация завершена успешно"

# Ждем завершения PostgreSQL
wait $POSTGRES_PID