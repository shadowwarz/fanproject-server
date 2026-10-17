#!/usr/bin/env bash
# ==============================================================================
# Project Durango: Wild Lands - Mulerun VPS Deployment & Server Launcher
# Repository: https://github.com/shadowwarz/fanproject-server.git
# ==============================================================================

set -e

# Resolve repository and script directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Configuration Defaults (as requested for Mulerun VPS)
GATEWAY_PORT="${GATEWAY_PORT:-8190}"
GAME_PORT="${GAME_PORT:-8191}"
PUBLIC_HOST="${PUBLIC_HOST:-47.79.240.13}"
CLUSTER_MODE="${CLUSTER_MODE:-Online}"
MAX_PLAYERS="${MAX_PLAYERS:-200}"
TPS="${TPS:-120}"
ADMIN_TOKEN="${DURANGO_ADMIN_TOKEN:-}"

# Colors
GREEN='\033[0;32m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

echo -e "${CYAN}================================================================${NC}"
echo -e "${GREEN}    Project Durango: Wild Lands - Mulerun VPS Server Launcher   ${NC}"
echo -e "${CYAN}================================================================${NC}"
echo -e "Repository Root:   $REPO_ROOT"
echo -e "Gateway HTTP Port: ${YELLOW}$GATEWAY_PORT${NC} (Client Handshake & Patch Server)"
echo -e "Game TCP Port:     ${YELLOW}$GAME_PORT${NC} (World Entity & Simulation Protocol)"
echo -e "Public Host:       ${YELLOW}$PUBLIC_HOST${NC}"
echo -e "Cluster Mode:      ${YELLOW}$CLUSTER_MODE${NC}"
echo -e "Max Players:       $MAX_PLAYERS"
echo -e "Ticks / Second:    $TPS TPS"
echo -e "${CYAN}----------------------------------------------------------------${NC}"

# Find or build DurangoServer
PUBLISH_DIR=""

# Check possible locations for pre-published binary
if [ -f "$REPO_ROOT/server/publish/linux-x64/DurangoServer" ]; then
    PUBLISH_DIR="$REPO_ROOT/server/publish/linux-x64"
elif [ -f "$REPO_ROOT/publish/linux-x64/DurangoServer" ]; then
    PUBLISH_DIR="$REPO_ROOT/publish/linux-x64"
elif [ -f "$SCRIPT_DIR/linux-x64/DurangoServer" ]; then
    PUBLISH_DIR="$SCRIPT_DIR/linux-x64"
fi

# If no binary found, check if a tarball exists to extract
if [ -z "$PUBLISH_DIR" ]; then
    TARBALL=""
    for tb in \
        "$REPO_ROOT/server/publish/DurangoServer-linux-x64.tar.gz" \
        "$REPO_ROOT/publish/DurangoServer-linux-x64.tar.gz" \
        "$REPO_ROOT/DurangoServer-linux-x64.tar.gz" \
        "$REPO_ROOT/durango_server_linux_v5.2.1.tar.gz"; do
        if [ -f "$tb" ]; then
            TARBALL="$tb"
            break
        fi
    done

    if [ -n "$TARBALL" ]; then
        echo -e "${YELLOW}Extracting pre-built server package from $TARBALL...${NC}"
        mkdir -p "$REPO_ROOT/server/publish/linux-x64"
        tar -xzf "$TARBALL" -C "$REPO_ROOT/server/publish/linux-x64" --strip-components=1 2>/dev/null || \
        tar -xzf "$TARBALL" -C "$REPO_ROOT/server/publish/linux-x64"
        if [ -f "$REPO_ROOT/server/publish/linux-x64/DurangoServer" ]; then
            PUBLISH_DIR="$REPO_ROOT/server/publish/linux-x64"
        fi
    fi
fi

# If still no binary found, build from source using dotnet
if [ -z "$PUBLISH_DIR" ] || [ ! -f "$PUBLISH_DIR/DurangoServer" ]; then
    echo -e "${YELLOW}No pre-built binary found. Preparing to build self-contained server...${NC}"
    
    # Check if dotnet is installed
    if ! command -v dotnet >/dev/null 2>&1; then
        if [ -f "$HOME/.dotnet/dotnet" ]; then
            export DOTNET_ROOT="$HOME/.dotnet"
            export PATH="$PATH:$HOME/.dotnet"
        else
            echo -e "${YELLOW}dotnet CLI not found. Automatically installing .NET 9 SDK via official Microsoft installer...${NC}"
            mkdir -p /tmp/dotnet-installer
            curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-installer/dotnet-install.sh
            chmod +x /tmp/dotnet-installer/dotnet-install.sh
            /tmp/dotnet-installer/dotnet-install.sh --channel 9.0
            export DOTNET_ROOT="$HOME/.dotnet"
            export PATH="$PATH:$HOME/.dotnet"
        fi
    fi

    echo -e "${GREEN}Building and publishing DurangoServer (linux-x64 self-contained)...${NC}"
    PUBLISH_DIR="$REPO_ROOT/server/publish/linux-x64"
    mkdir -p "$PUBLISH_DIR"
    dotnet publish "$REPO_ROOT/server/DurangoServer.csproj" -c Release -r linux-x64 --self-contained -o "$PUBLISH_DIR"
fi

# Ensure executable permissions
chmod +x "$PUBLISH_DIR/DurangoServer"

# Verify core data assets directory
if [ ! -d "$PUBLISH_DIR/data/assets" ]; then
    if [ -d "$REPO_ROOT/server/data" ]; then
        echo -e "${YELLOW}Syncing data directory to $PUBLISH_DIR/data...${NC}"
        mkdir -p "$PUBLISH_DIR/data"
        cp -rn "$REPO_ROOT/server/data/"* "$PUBLISH_DIR/data/" 2>/dev/null || true
    else
        echo -e "${RED}ERROR: data directory missing from $PUBLISH_DIR/data and $REPO_ROOT/server/data${NC}"
        exit 1
    fi
fi

# Handle CLI Subcommands
ACTION="${1:-run}"

case "$ACTION" in
    --check|check)
        echo -e "${GREEN}Running server data and catalog verification...${NC}"
        cd "$PUBLISH_DIR"
        ./DurangoServer --check-data
        exit 0
        ;;

    --install-service|install-service)
        echo -e "${YELLOW}Installing systemd service 'durango.service'...${NC}"
        SERVICE_FILE="/etc/systemd/system/durango.service"
        if [ "$EUID" -ne 0 ]; then
            echo -e "${RED}Error: Installing systemd service requires sudo / root privileges.${NC}"
            exit 1
        fi
        cat <<EOF > "$SERVICE_FILE"
[Unit]
Description=Project Durango: Wild Lands Dedicated Server
After=network.target network-online.target
Wants=network-online.target

[Service]
Type=simple
User=${SUDO_USER:-$USER}
WorkingDirectory=$PUBLISH_DIR
ExecStart=$PUBLISH_DIR/DurangoServer --gateway-port $GATEWAY_PORT --game-port $GAME_PORT --public-host $PUBLIC_HOST --cluster-mode $CLUSTER_MODE --max-players $MAX_PLAYERS --tps $TPS
Restart=on-failure
RestartSec=5s
KillSignal=SIGTERM
TimeoutStopSec=30s
LimitNOFILE=65535
Environment="DURANGO_ADMIN_TOKEN=$ADMIN_TOKEN"

[Install]
WantedBy=multi-user.target
EOF
        systemctl daemon-reload
        systemctl enable durango.service
        echo -e "${GREEN}durango.service installed successfully!${NC}"
        echo "Use 'sudo systemctl start durango' to start the server."
        echo "Use 'sudo journalctl -u durango -f' to view live logs."
        exit 0
        ;;

    --daemon|daemon|start)
        PID_FILE="$PUBLISH_DIR/durango.pid"
        LOG_FILE="$PUBLISH_DIR/server.log"
        if [ -f "$PID_FILE" ] && kill -0 "$(cat "$PID_FILE")" 2>/dev/null; then
            echo -e "${YELLOW}DurangoServer is already running with PID $(cat "$PID_FILE")${NC}"
            exit 0
        fi
        echo -e "${GREEN}Starting DurangoServer in background (daemon mode)...${NC}"
        cd "$PUBLISH_DIR"
        nohup ./DurangoServer \
            --gateway-port "$GATEWAY_PORT" \
            --game-port "$GAME_PORT" \
            --public-host "$PUBLIC_HOST" \
            --cluster-mode "$CLUSTER_MODE" \
            --max-players "$MAX_PLAYERS" \
            --tps "$TPS" \
            ${ADMIN_TOKEN:+--admin-token "$ADMIN_TOKEN"} \
            > "$LOG_FILE" 2>&1 &
        echo $! > "$PID_FILE"
        echo -e "${GREEN}Server started with PID $(cat "$PID_FILE"). Logs: $LOG_FILE${NC}"
        exit 0
        ;;

    stop)
        PID_FILE="$PUBLISH_DIR/durango.pid"
        if [ -f "$PID_FILE" ]; then
            PID="$(cat "$PID_FILE")"
            if kill -0 "$PID" 2>/dev/null; then
                echo -e "${YELLOW}Stopping DurangoServer (PID $PID)...${NC}"
                kill -TERM "$PID"
                for i in {1..30}; do
                    if ! kill -0 "$PID" 2>/dev/null; then
                        break
                    fi
                    sleep 1
                done
                rm -f "$PID_FILE"
                echo -e "${GREEN}Server stopped successfully.${NC}"
                exit 0
            fi
            rm -f "$PID_FILE"
        fi
        echo -e "${YELLOW}Server is not running.${NC}"
        exit 0
        ;;

    status)
        PID_FILE="$PUBLISH_DIR/durango.pid"
        if [ -f "$PID_FILE" ] && kill -0 "$(cat "$PID_FILE")" 2>/dev/null; then
            echo -e "${GREEN}DurangoServer is running (PID: $(cat "$PID_FILE"))${NC}"
            curl -s "http://127.0.0.1:$GATEWAY_PORT/status" || true
            echo ""
        else
            echo -e "${YELLOW}DurangoServer is not running.${NC}"
        fi
        exit 0
        ;;

    run|*)
        # Normal foreground run
        cd "$PUBLISH_DIR"

        # Trap signals to ensure graceful shutdown and world save
        cleanup() {
            echo -e "\n${YELLOW}[Signal Caught] Shutting down Durango Server gracefully (saving world)...${NC}"
            if [ -n "$PID" ]; then
                kill -TERM "$PID" 2>/dev/null || true
                wait "$PID" 2>/dev/null || true
            fi
            echo -e "${GREEN}Durango Server terminated cleanly.${NC}"
            exit 0
        }
        trap cleanup SIGINT SIGTERM

        ARGS=(
            "--gateway-port" "$GATEWAY_PORT"
            "--game-port" "$GAME_PORT"
            "--public-host" "$PUBLIC_HOST"
            "--cluster-mode" "$CLUSTER_MODE"
            "--max-players" "$MAX_PLAYERS"
            "--tps" "$TPS"
        )

        if [ -n "$ADMIN_TOKEN" ]; then
            ARGS+=("--admin-token" "$ADMIN_TOKEN")
        fi

        # Pass any additional arguments
        if [ "$ACTION" != "run" ]; then
            ARGS+=("$@")
        elif [ $# -gt 1 ]; then
            shift
            ARGS+=("$@")
        fi

        echo -e "${GREEN}Starting DurangoServer executable...${NC}"
        echo -e "Executing: ./DurangoServer ${ARGS[*]}"
        ./DurangoServer "${ARGS[@]}" &
        PID=$!
        wait "$PID"
        ;;
esac
