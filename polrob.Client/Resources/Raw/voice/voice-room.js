(() => {
    "use strict";

    let room = null;
    let connected = false;
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

    function participantSnapshot(participant, isLocal) {
        const publication = microphonePublication(participant);
        return {
            identity: participant.identity,
            name: participant.name || participant.identity,
            isLocal,
            isSpeaking: activeSpeakerIdentities.has(participant.identity),
            hasMicrophoneTrack: Boolean(publication),
            // 원격 음소거는 상대의 송출 상태가 아니라 이 기기의 재생 설정입니다.
            isMuted: isLocal
                ? !participant.isMicrophoneEnabled
                : remoteMutedIdentities.has(participant.identity)
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

        participant.setVolume(remoteMutedIdentities.has(participant.identity) ? 0 : 1);
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
            attachAudio(track, participant);
            emitParticipants();
        });
        targetRoom.on(RoomEvent.TrackUnsubscribed, (track) => {
            detachAudio(track);
            emitParticipants();
        });
        targetRoom.on(RoomEvent.ParticipantConnected, (participant) => {
            participant.setVolume(remoteMutedIdentities.has(participant.identity) ? 0 : 1);
            emitParticipants();
        });
        targetRoom.on(RoomEvent.ParticipantDisconnected, () => emitParticipants());
        targetRoom.on(RoomEvent.ParticipantNameChanged, () => emitParticipants());
        targetRoom.on(RoomEvent.TrackMuted, () => emitParticipants());
        targetRoom.on(RoomEvent.TrackUnmuted, () => emitParticipants());
        targetRoom.on(RoomEvent.LocalTrackPublished, () => emitParticipants());
        targetRoom.on(RoomEvent.LocalTrackUnpublished, () => emitParticipants());
        targetRoom.on(RoomEvent.ActiveSpeakersChanged, (speakers) => {
            activeSpeakerIdentities.clear();
            for (const speaker of speakers) {
                activeSpeakerIdentities.add(speaker.identity);
            }
            emitParticipants();
        });
        targetRoom.on(RoomEvent.Reconnecting, () => emitConnection("reconnecting"));
        targetRoom.on(RoomEvent.Reconnected, () => {
            connected = true;
            for (const participant of targetRoom.remoteParticipants.values()) {
                participant.setVolume(remoteMutedIdentities.has(participant.identity) ? 0 : 1);
            }
            emitConnection("connected");
            emitParticipants();
        });
        targetRoom.on(RoomEvent.Disconnected, () => {
            connected = false;
            activeSpeakerIdentities.clear();
            detachAllAudio();
            emitConnection("disconnected");
            emitParticipants();
        });
        targetRoom.on(RoomEvent.AudioPlaybackStatusChanged, async () => {
            if (!targetRoom.canPlayAudio) {
                try {
                    await targetRoom.startAudio();
                } catch (error) {
                    send({ type: "warning", message: `음성 재생을 시작하지 못했습니다: ${errorText(error)}` });
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

        const { Room } = window.LivekitClient;
        room = new Room({
            adaptiveStream: false,
            dynacast: false,
            audioCaptureDefaults: {
                echoCancellation: true,
                noiseSuppression: true,
                autoGainControl: true
            }
        });
        registerRoomEvents(room);
        emitConnection("connecting");

        try {
            await room.connect(command.url, command.token, { autoSubscribe: true });
            connected = true;

            // 권한이 거부된 경우에는 마이크를 발행하지 않고 듣기 전용으로 접속합니다.
            if (command.enableMicrophone) {
                try {
                    await room.localParticipant.setMicrophoneEnabled(true);
                } catch (error) {
                    send({ type: "warning", message: `마이크를 켜지 못했습니다: ${errorText(error)}` });
                }
            }

            try {
                await room.startAudio();
            } catch (error) {
                send({ type: "warning", message: `음성 재생을 시작하지 못했습니다: ${errorText(error)}` });
            }

            emitConnection("connected");
            emitParticipants();
        } catch (error) {
            connected = false;
            emitConnection("error", errorText(error));
            if (room) {
                await room.disconnect();
            }
            room = null;
            throw error;
        }
    }

    async function setLocalMuted(muted) {
        if (!room || !connected) {
            throw new Error("팀 보이스에 연결되어 있지 않습니다.");
        }
        await room.localParticipant.setMicrophoneEnabled(!muted);
        emitParticipants();
    }

    function setRemoteMuted(identity, muted) {
        if (muted) {
            remoteMutedIdentities.add(identity);
        } else {
            remoteMutedIdentities.delete(identity);
        }

        const participant = room ? room.remoteParticipants.get(identity) : undefined;
        if (participant) {
            // 볼륨 조절은 이 WebView의 재생에만 적용되므로 상대나 다른 팀원에게 영향이 없습니다.
            participant.setVolume(muted ? 0 : 1);
        }
        emitParticipants();
    }

    async function disconnect() {
        if (room) {
            await room.disconnect();
            room = null;
        }
        connected = false;
        remoteMutedIdentities.clear();
        activeSpeakerIdentities.clear();
        detachAllAudio();
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
        void handleCommand(event.detail.message);
    });

    // 이 메시지를 받은 뒤에만 C#이 명령을 보내므로 초기화 중 메시지 손실을 막습니다.
    send({ type: "ready" });
})();
