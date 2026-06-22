#!/usr/bin/env sh
set -eu

if [ -z "${DATABASE_URL:-}" ]; then
  printf "DATABASE_URL: " >&2
  if [ -t 0 ]; then stty -echo; fi
  IFS= read -r DATABASE_URL
  if [ -t 0 ]; then stty echo; printf "\n" >&2; fi
  export DATABASE_URL
fi

export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"
export PORT="${PORT:-5088}"
exec dotnet run --project src/Mirage.Api --no-build
