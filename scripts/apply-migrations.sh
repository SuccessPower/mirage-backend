#!/usr/bin/env sh
set -eu

if [ -z "${DATABASE_URL:-}" ]; then
  printf "DATABASE_URL: " >&2
  if [ -t 0 ]; then stty -echo; fi
  IFS= read -r DATABASE_URL
  if [ -t 0 ]; then stty echo; printf "\n" >&2; fi
  export DATABASE_URL
fi

dotnet ef database update \
  --project src/Mirage.Infrastructure \
  --startup-project src/Mirage.Infrastructure \
  --framework net8.0 \
  --no-build
