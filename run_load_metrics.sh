#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOGDIR="${POLROB_LOAD_LOGDIR:-/tmp/polrob-load-$(date +%Y%m%d-%H%M%S)}"
RESULT_CSV="$LOGDIR/results.csv"
RESULT_MD="$LOGDIR/results.md"

BOT_COUNTS_TEXT="${POLROB_LOAD_BOT_COUNTS:-60 300 600 900}"
read -r -a BOT_COUNTS <<< "$BOT_COUNTS_TEXT"

TARGET_ELIGIBLE="${POLROB_LOAD_ELIGIBLE_SAMPLES:-45}"
WINDOW="${POLROB_LOAD_WINDOW_SECONDS:-20}"
STAGGER_MS="${POLROB_BOT_GAMEPLAY_CONNECT_STAGGER_MS:-10}"
INITIAL_TIMEOUT_SECONDS="${POLROB_BOT_INITIAL_STATE_TIMEOUT_SECONDS:-240}"
MAX_WAIT_SECONDS="${POLROB_LOAD_MAX_WAIT_SECONDS:-360}"
SERVER_THREAD_POOL_MIN_THREADS="${POLROB_SERVER_THREAD_POOL_MIN_THREADS:-1200}"
BOT_THREAD_POOL_MIN_THREADS="${POLROB_BOT_THREAD_POOL_MIN_THREADS:-1600}"

mkdir -p "$LOGDIR"

HEADER="bots,expected_rooms,eligible_samples,window_start_sample,udp_recv_per_s,udp_send_per_s,udp_recv_bytes_per_s,udp_send_bytes_per_s,udp_recv_avg_bytes,udp_send_avg_bytes,tcp_send_per_s,json_serialize_per_s,connections,players,rooms,waiting_rooms,countdown_rooms,playing_rooms,ended_rooms,game_tcp_players,game_tcp_rooms,lobby_players,lobby_rooms,random_players,random_rooms,random_matched_rooms,random_in_game_rooms,exceptions_per_s,gc_collections_per_s,gc_alloc_mb_per_s,gc_pause_ms_per_s,lock_contentions_per_s,cpu_s_per_s,working_set_mb,tp_queue,tp_threads,network_total_per_s,udp_total_bytes_per_s,bot_failures,bot_connected,server_log,bot_log"
printf '%s\n' "$HEADER" > "$RESULT_CSV"

stop_pid() {
  local pid="${1:-}"
  if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
    kill "$pid" 2>/dev/null || true
    for _ in $(seq 1 30); do
      if ! kill -0 "$pid" 2>/dev/null; then
        wait "$pid" 2>/dev/null || true
        return 0
      fi
      sleep 0.2
    done
    kill -9 "$pid" 2>/dev/null || true
    wait "$pid" 2>/dev/null || true
  fi
}

cleanup_ports() {
  local pids
  pids="$(lsof -nP -tiTCP:5174 -tiTCP:7777 -tiUDP:7778 2>/dev/null | sort -u || true)"
  if [[ -n "$pids" ]]; then
    for pid in $pids; do
      stop_pid "$pid"
    done
  fi
}

wait_for_server() {
  local pid="$1"
  for _ in $(seq 1 120); do
    if ! kill -0 "$pid" 2>/dev/null; then
      return 1
    fi
    if lsof -nP -iTCP:5174 -sTCP:LISTEN >/dev/null 2>&1; then
      return 0
    fi
    sleep 0.5
  done
  return 1
}

count_eligible_samples() {
  local logfile="$1"
  local count="$2"
  local rooms="$3"

  awk -v players="$count" -v rooms="$rooms" '
    /\[LoadMetrics\]/ {
      delete kv
      for (i = 1; i <= NF; i++) {
        split($i, p, "=")
        if (length(p[1]) && length(p[2])) kv[p[1]] = p[2] + 0
      }
      if (kv["players"] == players &&
          kv["game_tcp_rooms"] == rooms &&
          kv["playing_rooms"] == rooms &&
          kv["random_in_game_rooms"] == rooms) {
        n++
      }
    }
    END { print n + 0 }
  ' "$logfile" 2>/dev/null || printf '0\n'
}

append_best_window_summary() {
  local logfile="$1"
  local botlog="$2"
  local count="$3"
  local rooms="$4"
  local bot_failures
  local bot_connected

  bot_failures="$(grep -c '^\[봇 실패\]' "$botlog" 2>/dev/null || true)"
  bot_connected="$(grep -c '게임 접속 완료' "$botlog" 2>/dev/null || true)"

  awk \
    -v count="$count" \
    -v rooms="$rooms" \
    -v window="$WINDOW" \
    -v bot_failures="$bot_failures" \
    -v bot_connected="$bot_connected" \
    -v server_log="$logfile" \
    -v bot_log="$botlog" '
    BEGIN {
      keys = "udp_recv/s udp_send/s udp_recv_bytes/s udp_send_bytes/s udp_recv_avg_bytes udp_send_avg_bytes tcp_send/s json_serialize/s connections players rooms waiting_rooms countdown_rooms playing_rooms ended_rooms game_tcp_players game_tcp_rooms lobby_players lobby_rooms random_players random_rooms random_matched_rooms random_in_game_rooms exceptions/s gc_collections/s gc_alloc_mb/s gc_pause_ms/s lock_contentions/s cpu_s/s working_set_mb tp_queue tp_threads"
      split(keys, keyList, " ")
    }
    /\[LoadMetrics\]/ {
      delete kv
      for (i = 1; i <= NF; i++) {
        split($i, p, "=")
        if (length(p[1]) && length(p[2])) kv[p[1]] = p[2] + 0
      }
      if (kv["players"] == count &&
          kv["game_tcp_rooms"] == rooms &&
          kv["playing_rooms"] == rooms &&
          kv["random_in_game_rooms"] == rooms) {
        n++
        for (i = 1; i <= length(keyList); i++) {
          value[n, keyList[i]] = kv[keyList[i]]
        }
        total[n] = kv["udp_recv/s"] + kv["udp_send/s"] + kv["tcp_send/s"]
        bytesTotal[n] = kv["udp_recv_bytes/s"] + kv["udp_send_bytes/s"]
      }
    }
    END {
      bestStart = 1
      bestScore = -1
      usableWindow = window
      if (n < usableWindow) usableWindow = n

      if (usableWindow > 0) {
        for (start = 1; start <= n - usableWindow + 1; start++) {
          score = 0
          for (j = start; j < start + usableWindow; j++) score += total[j]
          if (score > bestScore) {
            bestScore = score
            bestStart = start
          }
        }
      }

      printf "%d,%d,%d,%d", count, rooms, n, bestStart
      for (i = 1; i <= length(keyList); i++) {
        key = keyList[i]
        sum = 0
        for (j = bestStart; j < bestStart + usableWindow; j++) sum += value[j, key]
        if (usableWindow > 0) printf ",%.2f", sum / usableWindow
        else printf ","
      }

      if (usableWindow > 0) {
        bytesScore = 0
        for (j = bestStart; j < bestStart + usableWindow; j++) bytesScore += bytesTotal[j]
        printf ",%.2f,%.2f", bestScore / usableWindow, bytesScore / usableWindow
      }
      else printf ",,"

      printf ",%d,%d,%s,%s\n", bot_failures, bot_connected, server_log, bot_log
    }
  ' "$logfile" >> "$RESULT_CSV"
}

write_markdown_summary() {
  {
    printf '# PolRob Load Metrics\n\n'
    printf -- '- logdir: `%s`\n' "$LOGDIR"
    printf -- '- bot counts: `%s`\n' "$BOT_COUNTS_TEXT"
    printf -- '- selected window: highest `udp_recv + udp_send + tcp_send` %ss window while every room is in `Playing`\n\n' "$WINDOW"

    printf '## Traffic\n\n'
    printf '| Bots | Rooms | UDP recv/s | UDP send/s | TCP send/s | JSON/s | Connections | Players | Total packets/s | Bot failures |\n'
    printf '|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|\n'
    awk -F, 'NR > 1 {
      printf "| %d | %d | %.0f | %.0f | %.0f | %.0f | %.0f | %.0f | %.0f | %d |\n",
        $1, $2, $5, $6, $11, $12, $13, $14, $37, $39
    }' "$RESULT_CSV"

    printf '\n## UDP Bytes\n\n'
    printf '| Bots | UDP recv bytes/s | UDP send bytes/s | Total UDP bytes/s | Avg recv bytes | Avg send bytes |\n'
    printf '|---:|---:|---:|---:|---:|---:|\n'
    awk -F, 'NR > 1 {
      printf "| %d | %.0f | %.0f | %.0f | %.1f | %.1f |\n",
        $1, $7, $8, $38, $9, $10
    }' "$RESULT_CSV"

    printf '\n## Runtime\n\n'
    printf '| Bots | CPU s/s | Working set MB | GC alloc MB/s | GC/s | GC pause ms/s | Lock/s | TP queue | TP threads |\n'
    printf '|---:|---:|---:|---:|---:|---:|---:|---:|---:|\n'
    awk -F, 'NR > 1 {
      printf "| %d | %.2f | %.0f | %.2f | %.2f | %.2f | %.2f | %.2f | %.0f |\n",
        $1, $33, $34, $30, $29, $31, $32, $35, $36
    }' "$RESULT_CSV"

    printf '\n## Raw Logs\n\n'
    awk -F, 'NR > 1 {
      printf "- %d bots: server `%s`, bot `%s`\n", $1, $41, $42
    }' "$RESULT_CSV"
  } > "$RESULT_MD"
}

trap cleanup_ports EXIT

cleanup_ports

printf 'Building server/test with ThreadPoolMinThreads...\n'
dotnet build "$ROOT/polrob.Server/polrob.Server.csproj" -p:ThreadPoolMinThreads="$SERVER_THREAD_POOL_MIN_THREADS"
dotnet build "$ROOT/polrob.Test/polrob.Test.csproj" -p:ThreadPoolMinThreads="$BOT_THREAD_POOL_MIN_THREADS"

printf 'logdir=%s\n' "$LOGDIR"

for count in "${BOT_COUNTS[@]}"; do
  rooms=$((count / 6))
  server_log="$LOGDIR/server-${count}.log"
  bot_log="$LOGDIR/bot-${count}.log"

  printf '\n=== bots=%d rooms=%d ===\n' "$count" "$rooms"

  (
    cd "$ROOT/polrob.Server"
    ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS=http://0.0.0.0:5174 \
    dotnet bin/Debug/net10.0/polrob.Server.dll
  ) > "$server_log" 2>&1 &
  server_pid=$!

  if ! wait_for_server "$server_pid"; then
    printf 'Server failed for bots=%d\n' "$count"
    tail -80 "$server_log" || true
    stop_pid "$server_pid"
    append_best_window_summary "$server_log" "$bot_log" "$count" "$rooms"
    continue
  fi

  (
    cd "$ROOT/polrob.Test"
    POLROB_BOT_COUNT="$count" \
    POLROB_BOT_GAMEPLAY_CONNECT_STAGGER_MS="$STAGGER_MS" \
    POLROB_BOT_INITIAL_STATE_TIMEOUT_SECONDS="$INITIAL_TIMEOUT_SECONDS" \
    dotnet bin/Debug/net10.0/polrob.Test.dll
  ) > "$bot_log" 2>&1 &
  bot_pid=$!

  start_time="$(date +%s)"
  last_report=0
  while true; do
    eligible="$(count_eligible_samples "$server_log" "$count" "$rooms")"
    now="$(date +%s)"
    elapsed=$((now - start_time))

    if [[ "$eligible" -ge "$TARGET_ELIGIBLE" ]]; then
      printf 'eligible samples collected: %s after %ss\n' "$eligible" "$elapsed"
      break
    fi

    if ! kill -0 "$bot_pid" 2>/dev/null; then
      printf 'bot process exited after %ss with %s eligible samples\n' "$elapsed" "$eligible"
      break
    fi

    if [[ "$elapsed" -ge "$MAX_WAIT_SECONDS" ]]; then
      printf 'timeout after %ss with %s eligible samples\n' "$elapsed" "$eligible"
      break
    fi

    if [[ $((elapsed - last_report)) -ge 15 ]]; then
      printf 'progress bots=%d elapsed=%ss eligible=%s/%d\n' "$count" "$elapsed" "$eligible" "$TARGET_ELIGIBLE"
      last_report="$elapsed"
    fi

    sleep 1
  done

  append_best_window_summary "$server_log" "$bot_log" "$count" "$rooms"

  stop_pid "$bot_pid"
  sleep 1
  stop_pid "$server_pid"
  sleep 2
  cleanup_ports
done

write_markdown_summary

ln -sfn "$LOGDIR" /tmp/polrob-load-latest

printf '\n=== CSV ===\n'
cat "$RESULT_CSV"
printf '\n=== Markdown ===\n'
cat "$RESULT_MD"
printf '\n\nSaved:\n- %s\n- %s\n' "$RESULT_CSV" "$RESULT_MD"
printf '- /tmp/polrob-load-latest/results.csv\n- /tmp/polrob-load-latest/results.md\n'
