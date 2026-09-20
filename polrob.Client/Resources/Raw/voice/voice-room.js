(() => {
    "use strict";

    let room = null;
    let connected = false;
    let connectionGeneration = 0;
    let commandQueue = Promise.resolve();
    let playbackVolume = 1;
    const remoteMutedIdentities = new Set();
    const activeSpeakerIdentities = new Set();
    const attachedAudioElements = new Map();

    function send(message) {
        window.HybridWebView.SendRawMessage(JSON.stringify(message));
    }

    function errorText(error) {
        if (error instanceof Error && error.message) {
            return error.message;
        }
        return String(error || "알 수 없는 LiveKit 오류입니다.");
    }

    function microphonePublication(participant) {
        if (!participant || !window.LivekitClient) {
            return undefined;
        }
        return participant.getTrackPublication(window.LivekitClient.Track.Source.Microphone);
    }

    function gameIdentity(participant) {
        try {
            const metadata = JSON.parse(participant.metadata || "{}");
            if (typeof metadata.gameUserId === "string" && metadata.gameUserId.length > 0) {
                return metadata.gameUserId;
            }
        } catch {
            // 이전 서버 토큰과도 연결될 수 있도록 LiveKit identity를 대체값으로 사용합니다.
        }
        return participant.identity;
    }

    function participantSnapshot(participant, isLocal) {
        const publication = microphonePublication(participant);
        const identity = gameIdentity(participant);
        return {
            identity,
            name: participant.name || identity,
            isLocal,
            isSpeaking: activeSpeakerIdentities.has(participant.identity),
            hasMicrophoneTrack: Boolean(publication),
            // 원격 음소거는 상대의 송출 상태가 아니라 이 기기의 재생 설정입니다.
            isMuted: isLocal
                ? !participant.isMicrophoneEnabled
                : remoteMutedIdentities.has(identity)
        };
    }

    function emitParticipants() {
        if (!room || !connected) {
            send({ type: "participants", participants: [] });
            return;
        }

        const participants = [participantSnapshot(room.localParticipant, true)];
        for (const participant of room.remoteParticipants.values()) {
            participants.push(participantSnapshot(participant, false));
        }
        send({ type: "participants", participants });
    }

    function emitConnection(state, message) {
        send({ type: "connection", state, message: message || null });
    }

    function isCurrentRoom(targetRoom) {
        return room === targetRoom;
    }

    function attachAudio(track, participant) {
        const { Track } = window.LivekitClient;
        if (track.kind !== Track.Kind.Audio) {
            return;
        }

        const key = track.sid || `${participant.identity}-microphone`;
        if (attachedAudioElements.has(key)) {
            return;
        }

        const element = track.attach();
        element.autoplay = true;
        element.playsInline = true;
        element.dataset.livekitTrack = key;
        document.getElementById("audio-root").appendChild(element);
        attachedAudioElements.set(key, { track, element });

        participant.setVolume(remoteMutedIdentities.has(gameIdentity(participant)) ? 0 : playbackVolume);
    }

    function detachAudio(track) {
        const key = track.sid;
        const attached = key ? attachedAudioElements.get(key) : undefined;
        if (!attached) {
            return;
        }

        attached.track.detach(attached.element);
        attached.element.remove();
        attachedAudioElements.delete(key);
    }

    function detachAllAudio() {
        for (const { track, element } of attachedAudioElements.values()) {
            track.detach(element);
            element.remove();
        }
        attachedAudioElements.clear();
    }

    function registerRoomEvents(targetRoom) {
        const { RoomEvent } = window.LivekitClient;

        targetRoom.on(RoomEvent.TrackSubscribed, (track, _publication, participant) => {
            if (!isCurrentRoom(targetRoom)) {
                return;
            }
            attachAudio(track, participant);
            emitParticipants();
        });
        targetRoom.on(RoomEvent.TrackUnsubscribed, (track) => {
            if (!isCurrentRoom(targetRoom)) {
                return;
            }
            detachAudio(track);
            emitParticipants();
        });
        targetRoom.on(RoomEvent.ParticipantConnected, (participant) => {
            if (!isCurrentRoom(targetRoom)) {
                return;
            }
            participant.setVolume(remoteMutedIdentities.has(gameIdentity(participant)) ? 0 : playbackVolume);
            emitParticipants();
        });
        targetRoom.on(RoomEvent.ParticipantDisconnected, () => {
            if (isCurrentRoom(targetRoom)) emitParticipants();
        });
        targetRoom.on(RoomEvent.ParticipantNameChanged, () => {
            if (isCurrentRoom(targetRoom)) emitParticipants();
        });
        targetRoom.on(RoomEvent.ParticipantMetadataChanged, () => {
            if (isCurrentRoom(targetRoom)) emitParticipants();
        });
        targetRoom.on(RoomEvent.TrackMuted, () => {
            if (isCurrentRoom(targetRoom)) emitParticipants();
        });
        targetRoom.on(RoomEvent.TrackUnmuted, () => {
            if (isCurrentRoom(targetRoom)) emitParticipants();
        });
        targetRoom.on(RoomEvent.LocalTrackPublished, () => {
            if (isCurrentRoom(targetRoom)) emitParticipants();
        });
        targetRoom.on(RoomEvent.LocalTrackUnpublished, () => {
            if (isCurrentRoom(targetRoom)) emitParticipants();
        });
        targetRoom.on(RoomEvent.ActiveSpeakersChanged, (speakers) => {
            if (!isCurrentRoom(targetRoom)) {
                return;
            }
            activeSpeakerIdentities.clear();
            for (const speaker of speakers) {
                activeSpeakerIdentities.add(speaker.identity);
            }
            emitParticipants();
        });
        targetRoom.on(RoomEvent.Reconnecting, () => {
            if (isCurrentRoom(targetRoom)) emitConnection("reconnecting");
        });
        targetRoom.on(RoomEvent.Reconnected, () => {
            if (!isCurrentRoom(targetRoom)) {
                return;
            }
            connected = true;
            for (const participant of targetRoom.remoteParticipants.values()) {
                participant.setVolume(remoteMutedIdentities.has(gameIdentity(participant)) ? 0 : playbackVolume);
            }
            emitConnection("connected");
            emitParticipants();
        });
        targetRoom.on(RoomEvent.Disconnected, () => {
            if (!isCurrentRoom(targetRoom)) {
                return;
            }
            room = null;
            connected = false;
            activeSpeakerIdentities.clear();
            detachAllAudio();
            emitConnection("disconnected");
            emitParticipants();
        });
        targetRoom.on(RoomEvent.AudioPlaybackStatusChanged, async () => {
            if (isCurrentRoom(targetRoom) && !targetRoom.canPlayAudio) {
                try {
                    await targetRoom.startAudio();
                } catch (error) {
                    if (isCurrentRoom(targetRoom)) {
                        send({ type: "warning", message: `음성 재생을 시작하지 못했습니다: ${errorText(error)}` });
                    }
                }
            }
        });
    }

    async function connect(command) {
        if (window.__liveKitScriptFailed || !window.LivekitClient) {
            throw new Error("LiveKit 클라이언트 SDK를 불러오지 못했습니다. 인터넷 연결을 확인하세요.");
        }

        if (room) {
            await disconnect();
        }

        playbackVolume = Math.min(1, Math.max(0, Number(command.playbackVolume ?? 1)));
        const generation = ++connectionGeneration;
        const { Room } = window.LivekitClient;
        const targetRoom = new Room({
            adaptiveStream: false,
            dynacast: false,
            audioCaptureDefaults: {
                echoCancellation: true,
                noiseSuppression: true,
                autoGainControl: true
            }
        });
        room = targetRoom;
        registerRoomEvents(targetRoom);
        emitConnection("connecting");

        try {
            await targetRoom.connect(command.url, command.token, { autoSubscribe: true });
            if (generation !== connectionGeneration || !isCurrentRoom(targetRoom)) {
                await targetRoom.disconnect();
                throw new Error("팀 보이스 연결이 취소되었습니다.");
            }
            connected = true;

            // 권한이 거부된 경우에는 마이크를 발행하지 않고 듣기 전용으로 접속합니다.
            if (command.enableMicrophone) {
                try {
                    await targetRoom.localParticipant.setMicrophoneEnabled(true);
                } catch (error) {
                    if (isCurrentRoom(targetRoom)) {
                        send({ type: "warning", message: `마이크를 켜지 못했습니다: ${errorText(error)}` });
                    }
                }
            }

            try {
                await targetRoom.startAudio();
            } catch (error) {
                if (isCurrentRoom(targetRoom)) {
                    send({ type: "warning", message: `음성 재생을 시작하지 못했습니다: ${errorText(error)}` });
                }
            }

            if (isCurrentRoom(targetRoom)) {
                emitConnection("connected");
                emitParticipants();
            }
        } catch (error) {
            connected = false;
            if (generation === connectionGeneration) {
                emitConnection("error", errorText(error));
            }
            if (isCurrentRoom(targetRoom)) {
                room = null;
            }
            try {
                await targetRoom.disconnect();
            } catch {
                // 원래 연결 오류를 유지합니다.
            }
            throw error;
        }
    }

    async function setLocalMuted(muted) {
        if (!room || !connected) {
            throw new Error("팀 보이스에 연결되어 있지 않습니다.");
        }
        const activeRoom = room;
        await activeRoom.localParticipant.setMicrophoneEnabled(!muted);
        if (!isCurrentRoom(activeRoom)) {
            throw new Error("팀 보이스 연결이 변경되었습니다.");
        }
        emitParticipants();
    }

    function setRemoteMuted(identity, muted) {
        if (!room || !connected) {
            throw new Error("팀 보이스에 연결되어 있지 않습니다.");
        }
        if (!identity) {
            throw new Error("음소거할 참가자 ID가 없습니다.");
        }

        const activeRoom = room;
        const participant = [...activeRoom.remoteParticipants.values()]
            .find(candidate => gameIdentity(candidate) === identity);
        if (!participant) {
            throw new Error("해당 팀원이 보이스 채널에 연결되어 있지 않습니다.");
        }

        if (muted) {
            remoteMutedIdentities.add(identity);
        } else {
            remoteMutedIdentities.delete(identity);
        }

        // 볼륨 조절은 이 WebView의 재생에만 적용되므로 상대나 다른 팀원에게 영향이 없습니다.
        participant.setVolume(muted ? 0 : playbackVolume);
        if (!isCurrentRoom(activeRoom)) {
            throw new Error("팀 보이스 연결이 변경되었습니다.");
        }
        emitParticipants();
    }

    function setPlaybackVolume(volume) {
        playbackVolume = Math.min(1, Math.max(0, Number(volume)));
        if (!Number.isFinite(playbackVolume)) {
            playbackVolume = 1;
        }

        if (!room || !connected) {
            return;
        }

        for (const participant of room.remoteParticipants.values()) {
            participant.setVolume(
                remoteMutedIdentities.has(gameIdentity(participant)) ? 0 : playbackVolume);
        }
    }

    async function disconnect() {
        connectionGeneration++;
        const activeRoom = room;
        room = null;
        connected = false;
        // 같은 게임 화면이 백그라운드에서 돌아온 경우 사용자가 정한 원격 음소거를 유지합니다.
        activeSpeakerIdentities.clear();
        detachAllAudio();

        if (activeRoom) {
            await activeRoom.disconnect();
        }

        emitConnection("disconnected");
        emitParticipants();
    }

    async function handleCommand(rawMessage) {
        let command;
        try {
            command = JSON.parse(rawMessage);
        } catch {
            return;
        }

        const requestId = command.requestId;
        try {
            switch (command.type) {
                case "connect":
                    await connect(command);
                    break;
                case "setLocalMuted":
                    await setLocalMuted(Boolean(command.muted));
                    break;
                case "setRemoteMuted":
                    setRemoteMuted(String(command.identity || ""), Boolean(command.muted));
                    break;
                case "setPlaybackVolume":
                    setPlaybackVolume(command.volume);
                    break;
                case "disconnect":
                    await disconnect();
                    break;
                default:
                    throw new Error(`지원하지 않는 보이스 명령입니다: ${command.type}`);
            }
            send({ type: "commandResult", requestId, success: true });
        } catch (error) {
            send({ type: "commandResult", requestId, success: false, error: errorText(error) });
        }
    }

    window.addEventListener("HybridWebViewMessageReceived", (event) => {
        const rawMessage = event.detail.message;
        let commandType;
        try {
            commandType = JSON.parse(rawMessage).type;
        } catch {
            return;
        }

        // connect가 네트워크에서 오래 대기 중이어도 화면 이탈은 즉시 Room을 닫아야 합니다.
        if (commandType === "disconnect") {
            void handleCommand(rawMessage);
            return;
        }

        commandQueue = commandQueue
            .then(() => handleCommand(rawMessage))
            .catch((error) => {
                send({ type: "error", message: errorText(error) });
            });
    });

    // 이 메시지를 받은 뒤에만 C#이 명령을 보내므로 초기화 중 메시지 손실을 막습니다.
    send({ type: "ready" });
})();
