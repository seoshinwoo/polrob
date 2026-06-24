from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen import canvas


ROOT = Path("/Users/seoshinwoo/Documents/code/projects/polrob")
OUT = ROOT / "output" / "pdf" / "game_server_portfolio_seoshinwoo.pdf"

PAGE_W, PAGE_H = A4
MARGIN_X = 48
TOP = PAGE_H - 54
BOTTOM = 46
CONTENT_W = PAGE_W - MARGIN_X * 2

FONT = "AppleGothic"
FONT_SERIF = FONT
FONT_BOLD = FONT

INK = colors.HexColor("#161A1D")
MUTED = colors.HexColor("#5B6268")
LIGHT = colors.HexColor("#F4F6F8")
LINE = colors.HexColor("#D7DCE1")
BLUE = colors.HexColor("#2563EB")
NAVY = colors.HexColor("#0F172A")
GREEN = colors.HexColor("#0F766E")
AMBER = colors.HexColor("#B7791F")
RED = colors.HexColor("#B91C1C")
SOFT_BLUE = colors.HexColor("#EAF1FF")
SOFT_GREEN = colors.HexColor("#E8F7F3")
SOFT_AMBER = colors.HexColor("#FFF7E6")
SOFT_RED = colors.HexColor("#FEECEC")


pdfmetrics.registerFont(TTFont(FONT, "/System/Library/Fonts/Supplemental/AppleGothic.ttf"))


@dataclass
class PageState:
    number: int = 0


state = PageState()


def string_width(text: str, size: float, font: str = FONT) -> float:
    return pdfmetrics.stringWidth(text, font, size)


def wrap_text(text: str, width: float, size: float, font: str = FONT) -> list[str]:
    lines: list[str] = []
    for raw in text.split("\n"):
        raw = raw.strip()
        if not raw:
            lines.append("")
            continue
        current = ""
        for token in raw.split(" "):
            test = token if not current else f"{current} {token}"
            if string_width(test, size, font) <= width:
                current = test
                continue
            if current:
                lines.append(current)
            # For long Korean/technical tokens, fall back to char wrapping.
            if string_width(token, size, font) <= width:
                current = token
            else:
                chunk = ""
                for ch in token:
                    test_chunk = chunk + ch
                    if string_width(test_chunk, size, font) <= width:
                        chunk = test_chunk
                    else:
                        if chunk:
                            lines.append(chunk)
                        chunk = ch
                current = chunk
        if current:
            lines.append(current)
    return lines


def new_page(c: canvas.Canvas, section: str | None = None) -> None:
    if state.number:
        c.showPage()
    state.number += 1
    c.setFillColor(colors.white)
    c.rect(0, 0, PAGE_W, PAGE_H, fill=1, stroke=0)
    c.setFillColor(NAVY)
    c.rect(0, PAGE_H - 18, PAGE_W, 18, fill=1, stroke=0)
    c.setStrokeColor(LINE)
    c.line(MARGIN_X, BOTTOM - 12, PAGE_W - MARGIN_X, BOTTOM - 12)
    c.setFont(FONT, 8)
    c.setFillColor(MUTED)
    c.drawString(MARGIN_X, 24, "Game Server Portfolio - PolRob / CanvaSync")
    c.drawRightString(PAGE_W - MARGIN_X, 24, str(state.number))
    if section:
        c.setFont(FONT, 8)
        c.setFillColor(BLUE)
        c.drawRightString(PAGE_W - MARGIN_X, PAGE_H - 34, section)


def text(c: canvas.Canvas, x: float, y: float, body: str, size: float = 10, width: float | None = None,
         leading: float | None = None, color=INK, font: str = FONT) -> float:
    c.setFont(font, size)
    c.setFillColor(color)
    leading = leading or size * 1.5
    if width is None:
        c.drawString(x, y, body)
        return y - leading
    for line in wrap_text(body, width, size, font):
        c.drawString(x, y, line)
        y -= leading
    return y


def title(c: canvas.Canvas, y: float, label: str, subtitle: str | None = None) -> float:
    c.setFillColor(NAVY)
    c.setFont(FONT_BOLD, 22)
    c.drawString(MARGIN_X, y, label)
    y -= 18
    c.setStrokeColor(BLUE)
    c.setLineWidth(2)
    c.line(MARGIN_X, y, MARGIN_X + 90, y)
    y -= 18
    if subtitle:
        y = text(c, MARGIN_X, y, subtitle, 10, CONTENT_W, 15, MUTED)
    return y - 10


def chip(c: canvas.Canvas, x: float, y: float, label: str, bg=SOFT_BLUE, fg=BLUE) -> float:
    pad_x = 8
    w = string_width(label, 8.5) + pad_x * 2
    h = 18
    c.setFillColor(bg)
    c.roundRect(x, y - h + 4, w, h, 6, fill=1, stroke=0)
    c.setFillColor(fg)
    c.setFont(FONT, 8.5)
    c.drawString(x + pad_x, y - 9, label)
    return x + w + 6


def bullet_list(c: canvas.Canvas, x: float, y: float, items: Iterable[str], width: float,
                size: float = 9.4, gap: float = 7, bullet_color=BLUE) -> float:
    for item in items:
        c.setFillColor(bullet_color)
        c.circle(x + 3, y - 3, 2.2, fill=1, stroke=0)
        y = text(c, x + 13, y, item, size, width - 13, size * 1.45, INK)
        y -= gap
    return y


def section_header(c: canvas.Canvas, x: float, y: float, label: str, color=BLUE) -> float:
    c.setFillColor(color)
    c.rect(x, y - 4, 4, 17, fill=1, stroke=0)
    c.setFillColor(INK)
    c.setFont(FONT_BOLD, 13)
    c.drawString(x + 10, y, label)
    return y - 22


def card(c: canvas.Canvas, x: float, y: float, w: float, h: float, title_text: str, body: str,
         accent=BLUE, fill=LIGHT) -> None:
    c.setFillColor(fill)
    c.roundRect(x, y - h, w, h, 8, fill=1, stroke=0)
    c.setFillColor(accent)
    c.roundRect(x, y - h, 5, h, 2, fill=1, stroke=0)
    c.setFillColor(INK)
    c.setFont(FONT_BOLD, 11)
    c.drawString(x + 14, y - 22, title_text)
    text(c, x + 14, y - 42, body, 8.7, w - 26, 13, MUTED)


def metric_card(c: canvas.Canvas, x: float, y: float, w: float, h: float, metric: str, label: str, desc: str,
                accent=BLUE) -> None:
    c.setFillColor(colors.white)
    c.setStrokeColor(LINE)
    c.roundRect(x, y - h, w, h, 8, fill=1, stroke=1)
    c.setFillColor(accent)
    c.setFont(FONT, 18)
    c.drawString(x + 12, y - 26, metric)
    c.setFillColor(INK)
    c.setFont(FONT, 9.5)
    c.drawString(x + 12, y - 45, label)
    text(c, x + 12, y - 61, desc, 7.8, w - 24, 11, MUTED)


def two_col(c: canvas.Canvas, y: float, left_title: str, left_items: list[str], right_title: str,
            right_items: list[str], left_color=BLUE, right_color=GREEN) -> float:
    gap = 18
    w = (CONTENT_W - gap) / 2
    h = 190
    card(c, MARGIN_X, y, w, h, left_title, "", left_color, colors.white)
    card(c, MARGIN_X + w + gap, y, w, h, right_title, "", right_color, colors.white)
    yy = y - 48
    bullet_list(c, MARGIN_X + 16, yy, left_items, w - 30, 8.5, 5, left_color)
    bullet_list(c, MARGIN_X + w + gap + 16, yy, right_items, w - 30, 8.5, 5, right_color)
    return y - h - 20


def draw_architecture_polrob(c: canvas.Canvas, y: float) -> float:
    x = MARGIN_X
    c.setFont(FONT, 8)
    boxes = [
        ("MAUI Client", x, y, 82, 42, SOFT_BLUE, BLUE),
        ("HTTP / SignalR", x + 96, y, 92, 42, SOFT_GREEN, GREEN),
        ("TCP 7777", x + 202, y + 24, 74, 34, SOFT_AMBER, AMBER),
        ("UDP 7778", x + 202, y - 18, 74, 34, SOFT_AMBER, AMBER),
        ("Room Queue", x + 290, y, 78, 42, LIGHT, BLUE),
        ("Room Loop", x + 382, y, 76, 42, SOFT_BLUE, BLUE),
    ]
    for label, bx, by, bw, bh, fill, col in boxes:
        c.setFillColor(fill)
        c.setStrokeColor(col)
        c.roundRect(bx, by - bh, bw, bh, 7, fill=1, stroke=1)
        c.setFillColor(INK)
        c.drawCentredString(bx + bw / 2, by - bh / 2 - 3, label)
    c.setStrokeColor(MUTED)
    c.setLineWidth(1.2)
    for sx, sy, ex, ey in [
        (x + 82, y - 21, x + 96, y - 21),
        (x + 188, y - 21, x + 202, y + 7),
        (x + 188, y - 21, x + 202, y - 35),
        (x + 276, y + 7, x + 290, y - 21),
        (x + 276, y - 35, x + 290, y - 21),
        (x + 368, y - 21, x + 382, y - 21),
    ]:
        c.line(sx, sy, ex, ey)
    return y - 90


def draw_architecture_canvasync(c: canvas.Canvas, y: float) -> float:
    x = MARGIN_X
    boxes = [
        ("Browser", x, y, 80, 40, SOFT_BLUE, BLUE),
        ("ASP.NET + SignalR", x + 98, y, 120, 40, SOFT_GREEN, GREEN),
        ("PostgreSQL", x + 250, y + 36, 92, 34, LIGHT, BLUE),
        ("Redis/InMemory", x + 250, y - 5, 92, 34, LIGHT, GREEN),
        ("Azure Blob", x + 372, y + 15, 92, 34, LIGHT, AMBER),
    ]
    c.setFont(FONT, 8)
    for label, bx, by, bw, bh, fill, col in boxes:
        c.setFillColor(fill)
        c.setStrokeColor(col)
        c.roundRect(bx, by - bh, bw, bh, 7, fill=1, stroke=1)
        c.setFillColor(INK)
        c.drawCentredString(bx + bw / 2, by - bh / 2 - 3, label)
    c.setStrokeColor(MUTED)
    c.line(x + 80, y - 20, x + 98, y - 20)
    c.line(x + 218, y - 20, x + 250, y + 19)
    c.line(x + 218, y - 20, x + 250, y - 22)
    c.line(x + 218, y - 20, x + 372, y - 2)
    return y - 82


def cover(c: canvas.Canvas) -> None:
    new_page(c)
    c.setFillColor(NAVY)
    c.rect(0, PAGE_H - 245, PAGE_W, 245, fill=1, stroke=0)
    c.setFillColor(colors.white)
    c.setFont(FONT, 28)
    c.drawString(MARGIN_X, PAGE_H - 92, "Game Server Portfolio")
    c.setFont(FONT, 15)
    c.setFillColor(colors.HexColor("#C7D2FE"))
    c.drawString(MARGIN_X, PAGE_H - 122, "PolRob + CanvaSync")
    c.setFont(FONT, 10.5)
    c.setFillColor(colors.HexColor("#E5E7EB"))
    c.drawString(MARGIN_X, PAGE_H - 154, "신입 게임 서버 개발자 지원용 프로젝트 포트폴리오")
    c.drawString(MARGIN_X, PAGE_H - 174, "작성일: 2026. 06. 25")
    c.drawString(MARGIN_X, PAGE_H - 194, "지원자: Shinwoo Seo / Contact: 이력서 참조")

    y = PAGE_H - 292
    y = title(c, y, "Positioning", "실시간 게임 서버와 DB/클라우드 기반 실시간 협업 서비스를 함께 제시합니다.")
    card(c, MARGIN_X, y, CONTENT_W, 88, "핵심 요약",
         "PolRob은 TCP/UDP, 방 단위 루프, 서버 권위 게임 규칙, headless bot 부하 테스트를 통해 게임 서버 역량을 보여줍니다. "
         "CanvaSync는 PostgreSQL, Redis, Azure Blob Storage, Azure 배포 경험과 DB 무결성/인덱스 개선으로 서버 백엔드 역량을 보완합니다.",
         BLUE, SOFT_BLUE)
    y -= 116
    for label, bg, fg in [
        ("C# / .NET", SOFT_BLUE, BLUE),
        ("ASP.NET Core", SOFT_BLUE, BLUE),
        ("SignalR", SOFT_GREEN, GREEN),
        ("TCP / UDP", SOFT_AMBER, AMBER),
        ("PostgreSQL / JSONB", SOFT_BLUE, BLUE),
        ("Redis", SOFT_GREEN, GREEN),
        ("Azure", SOFT_BLUE, BLUE),
        ("Load Test", SOFT_AMBER, AMBER),
    ]:
        x = chip(c, MARGIN_X if label == "C# / .NET" else x, y, label, bg, fg) if "x" in locals() else chip(c, MARGIN_X, y, label, bg, fg)
    y -= 50
    y = section_header(c, MARGIN_X, y, "Portfolio Strategy")
    bullet_list(c, MARGIN_X, y, [
        "PolRob은 게임 서버 메인 프로젝트로 배치했습니다. 네트워크, concurrency, 서버 권위, 부하 측정이 중심입니다.",
        "CanvaSync는 DB/클라우드/실시간 협업 보조 프로젝트로 배치했습니다. 졸업작품 최우수상과 Azure 전시 배포 경험을 함께 제시합니다.",
        "대규모 상용 운영 경험으로 과장하지 않고, 직접 검증한 코드/배포/측정 결과만 근거로 삼았습니다.",
    ], CONTENT_W, 9.5)


def summary_page(c: canvas.Canvas) -> None:
    new_page(c, "Overview")
    y = title(c, TOP, "1. 지원 직무와 프로젝트 매핑", "모바일 게임 서버 개발 공고 기준으로 두 프로젝트의 증거를 역할별로 나눴습니다.")
    y = two_col(
        c, y,
        "PolRob - 게임 서버 역량",
        [
            "6인 비대칭 실시간 추격 게임 서버.",
            "HTTP, SignalR, raw TCP, UDP를 역할별로 분리.",
            "클라이언트는 조이스틱 입력만 전송하고 서버가 이동/충돌/체포/탈옥/승패를 판정.",
            "방별 bounded command queue와 single-consumer loop로 상태 변경 순서를 제어.",
            "headless bot으로 60/300/600/900 bots 부하 테스트와 최적화 비교.",
        ],
        "CanvaSync - DB/클라우드 역량",
        [
            "실시간 PDF 필기 동기화 웹 서비스.",
            "PostgreSQL, JSONB, Redis/InMemory, Azure Blob Storage 사용.",
            "Azure Cloud에 배포해 졸업작품 전시 환경에서 외부 사용자 사용 검증.",
            "졸업작품 최우수상 수상.",
            "unique index, foreign key, query-plan 문서로 DB 설계 증거 보강.",
        ],
    )
    y = section_header(c, MARGIN_X, y, "공고 키워드 대응")
    rows = [
        ("모바일 게임서버 개발", "PolRob", "인게임 TCP/UDP, 방 루프, 서버 권위 게임 규칙"),
        ("컨텐츠 데이터/DB", "CanvaSync", "PostgreSQL schema, JSONB, index, FK, EXPLAIN 문서"),
        ("라이브 유지보수/트러블슈팅", "PolRob + CanvaSync", "부하 테스트 리포트, Azure 전시 배포, 장애/성능 개선 기록"),
        ("네트워크/비동기", "PolRob", "SignalR, TCP/UDP, Channel<T>, ConcurrentDictionary, rate limit"),
        ("클라우드 환경 이해", "CanvaSync", "Azure 배포, Blob Storage, PostgreSQL/Redis 연결 구성"),
    ]
    draw_table(c, MARGIN_X, y, CONTENT_W, rows, [130, 110, CONTENT_W - 240])


def draw_table(
    c: canvas.Canvas,
    x: float,
    y: float,
    w: float,
    rows: list[tuple[str, str, str]],
    widths: list[float],
    headers: tuple[str, str, str] = ("요구 역량", "증거 프로젝트", "설명"),
) -> float:
    row_h = 42
    c.setFillColor(NAVY)
    c.roundRect(x, y - 26, w, 26, 6, fill=1, stroke=0)
    c.setFillColor(colors.white)
    c.setFont(FONT, 8.8)
    tx = x + 10
    for i, h in enumerate(headers):
        c.drawString(tx, y - 17, h)
        tx += widths[i]
    y -= 26
    for idx, row in enumerate(rows):
        c.setFillColor(colors.white if idx % 2 == 0 else LIGHT)
        c.rect(x, y - row_h, w, row_h, fill=1, stroke=0)
        c.setStrokeColor(LINE)
        c.line(x, y - row_h, x + w, y - row_h)
        tx = x + 10
        for i, val in enumerate(row):
            text(c, tx, y - 14, val, 8.2, widths[i] - 12, 11, INK if i == 0 else MUTED)
            tx += widths[i]
        y -= row_h
    return y - 16


def polrob_overview(c: canvas.Canvas) -> None:
    new_page(c, "PolRob")
    y = title(c, TOP, "2. PolRob - 실시간 모바일 게임 서버", "2명의 경찰과 4명의 도둑이 참여하는 6인 비대칭 추격 게임입니다.")
    y = draw_architecture_polrob(c, y)
    y = section_header(c, MARGIN_X, y, "역할과 설계 목표")
    y = bullet_list(c, MARGIN_X, y, [
        "개인 프로젝트로 모바일 클라이언트, ASP.NET 서버, raw TCP/UDP 네트워크 서버, 부하 테스트 봇을 직접 구현했습니다.",
        "신뢰성이 필요한 로비/인증/이벤트와 고빈도 이동 데이터를 분리해 HTTP, SignalR, TCP, UDP를 각각 사용했습니다.",
        "게임 규칙은 클라이언트 좌표를 신뢰하지 않고 서버에서 계산하도록 이동/충돌/체포/탈옥/승패 판정을 서버로 옮겼습니다.",
    ], CONTENT_W, 9.2)
    y -= 8
    y = section_header(c, MARGIN_X, y, "주요 구현 파일")
    files = [
        "polrob.Server/Network/GameNetworkServer*.cs",
        "polrob.Server/Network/GameSession.cs",
        "polrob.Shared/Models/PlayerMovementInput.cs",
        "polrob.Test/BotRunner.cs",
        "docs/server_optimization_report.md",
    ]
    x = MARGIN_X
    for f in files:
        x = chip(c, x, y, f, LIGHT, MUTED)
        if x > PAGE_W - MARGIN_X - 210:
            x = MARGIN_X
            y -= 25


def polrob_cases(c: canvas.Canvas) -> None:
    new_page(c, "PolRob")
    y = title(c, TOP, "3. PolRob - 문제 해결 사례", "게임 서버 개발자로 보여주고 싶은 핵심 구현을 AS-IS / TO-BE 형식으로 정리했습니다.")
    case_h = 155
    card(c, MARGIN_X, y, CONTENT_W, case_h, "Case 1. 클라이언트 좌표 신뢰 제거",
         "AS-IS: 클라이언트가 보낸 위치를 그대로 믿으면 조작, 순간이동, 충돌 무시 문제가 생길 수 있습니다.\n"
         "TO-BE: 클라이언트는 input vector와 sequence만 보내고, 서버가 속도/반지름/맵 충돌/시야/체포 판정을 계산합니다.\n"
         "검증: movement session token, UDP endpoint, sequence, NaN/Infinity, 맵 경계를 검사합니다.",
         BLUE, SOFT_BLUE)
    y -= case_h + 18
    card(c, MARGIN_X, y, CONTENT_W, case_h, "Case 2. 방 상태 동시 변경 문제",
         "AS-IS: TCP join/leave, UDP move, game rule tick이 동시에 상태를 바꾸면 복합 규칙 순서가 흐려집니다.\n"
         "TO-BE: 네트워크 수신부는 RoomCommand만 기록하고, 방별 single-consumer loop가 drain, simulation, rule tick, state sync를 처리합니다.\n"
         "결과: 방 단위로 상태 변경 순서를 격리하고, 이동 입력은 같은 플레이어의 최신 입력으로 coalescing합니다.",
         GREEN, SOFT_GREEN)
    y -= case_h + 18
    card(c, MARGIN_X, y, CONTENT_W, 116, "Case 3. 과부하와 비정상 입력 방어",
         "방 command queue는 bounded channel로 제한하고, UDP 입력에는 token-bucket rate limit을 적용했습니다. "
         "metrics 로그에 rate-limited, invalid, duplicate/late packet, queue length, dropped command를 노출해 부하 상황을 확인할 수 있게 했습니다.",
         AMBER, SOFT_AMBER)


def polrob_performance(c: canvas.Canvas) -> None:
    new_page(c, "PolRob")
    y = title(c, TOP, "4. PolRob - 부하 테스트와 최적화", "UI 시뮬레이터 대신 실제 프로토콜을 사용하는 headless bot으로 서버 병목을 측정했습니다.")
    metric_card(c, MARGIN_X, y, 122, 92, "900", "max bots", "150 rooms 기준 로컬 부하 테스트 구간", BLUE)
    metric_card(c, MARGIN_X + 142, y, 122, 92, "-78.9%", "CPU", "final optimized branch local comparison", GREEN)
    metric_card(c, MARGIN_X + 284, y, 122, 92, "-49.6%", "UDP bytes", "900 bots 구간 전체 UDP bytes/s", AMBER)
    metric_card(c, MARGIN_X + 426, y, 122, 92, "-56.4%", "Working Set", "900 bots 구간 memory footprint", RED)
    y -= 122
    y = section_header(c, MARGIN_X, y, "측정 지표")
    y = bullet_list(c, MARGIN_X, y, [
        "CPU, working set, GC allocation/pause, ThreadPool, lock contention",
        "UDP/TCP packets per second, UDP bytes per second, JSON serialization count",
        "connections, players, rooms, room phase, bot failures, eligible playing samples",
    ], CONTENT_W, 9.2)
    y -= 6
    y = section_header(c, MARGIN_X, y, "최적화 내용")
    rows = [
        ("Lightweight UDP payload", "패킷당 bytes와 JSON/GC 비용 감소"),
        ("Server send tick", "입력마다 즉시 broadcast 대신 방 tick에서 최신 snapshot 전송"),
        ("Input coalescing", "같은 플레이어의 오래된 move command를 최신 입력으로 병합"),
        ("Idle send-rate reduction", "정지 상태의 불필요한 클라이언트 입력 송신 감소"),
    ]
    y = draw_simple_rows(c, MARGIN_X, y, CONTENT_W, rows)
    y -= 16
    card(c, MARGIN_X, y, CONTENT_W, 76, "검증 범위",
         "수치는 동일 로컬 환경에서 baseline과 최적화 결과를 비교한 값입니다. 실제 상용 동시접속 수용량을 의미하지 않으며, 클라우드 VM, 네트워크 지연, packet loss는 별도 검증 대상입니다.",
         RED, SOFT_RED)


def draw_simple_rows(c: canvas.Canvas, x: float, y: float, w: float, rows: list[tuple[str, str]]) -> float:
    row_h = 32
    for i, (a, b) in enumerate(rows):
        c.setFillColor(colors.white if i % 2 == 0 else LIGHT)
        c.roundRect(x, y - row_h, w, row_h, 5, fill=1, stroke=0)
        c.setFillColor(BLUE)
        c.setFont(FONT, 8.8)
        c.drawString(x + 12, y - 20, a)
        text(c, x + 180, y - 20, b, 8.6, w - 195, 11, MUTED)
        y -= row_h + 5
    return y


def canvasync_overview(c: canvas.Canvas) -> None:
    new_page(c, "CanvaSync")
    y = title(c, TOP, "5. CanvaSync - 실시간 협업 서비스", "PDF 수업자료 위 교수자 필기를 학생 화면에 실시간 동기화하는 졸업작품입니다.")
    y = draw_architecture_canvasync(c, y)
    y = two_col(
        c, y,
        "서비스 성격",
        [
            "교수자가 PDF 위에 작성한 도형/텍스트/펜 필기를 강의방 학생에게 실시간 전파.",
            "학생은 교수자 필기와 분리된 개인 필기를 작성하고 병합 PDF를 다운로드.",
            "졸업작품 전시 환경에서 Azure 배포 후 외부 사용자 접속 흐름 검증.",
            "졸업작품 최우수상 수상.",
        ],
        "서버 관점의 가치",
        [
            "SignalR + MessagePack 기반 실시간 이벤트 동기화.",
            "PostgreSQL은 계정/강의/필기 snapshot 영속화.",
            "Redis/InMemory는 진행 중 필기 상태와 연결 정보를 저장.",
            "Azure Blob Storage에 PDF 원본 저장, DB에는 주소/메타데이터 보관.",
        ],
        BLUE,
        GREEN,
    )
    y = section_header(c, MARGIN_X, y, "주요 구현 파일")
    for label in [
        "canvasync/Hubs/CanvasHub.cs",
        "canvasync/Services/CanvasService.cs",
        "canvasync/Data/CanvasDbContext.cs",
        "canvasync/Controllers/PDFImagesController.cs",
        "docs/database-design-and-tuning.md",
    ]:
        y0 = y
        y = y0
        x = chip(c, MARGIN_X if label.startswith("canvasync/Hubs") else x, y, label, LIGHT, MUTED) if "x" in locals() else chip(c, MARGIN_X, y, label, LIGHT, MUTED)
        if x > PAGE_W - MARGIN_X - 230:
            x = MARGIN_X
            y -= 25


def canvasync_db(c: canvas.Canvas) -> None:
    new_page(c, "CanvaSync")
    y = title(c, TOP, "6. CanvaSync - DB 설계와 튜닝 근거", "단순 CRUD가 아니라 무결성, 접근 패턴, 조회 성능을 코드와 migration으로 보강했습니다.")
    rows = [
        ("Members.Name unique", "로그인/회원가입 조회 기준을 DB가 보장"),
        ("Lectures.Code unique", "6자리 입장 코드 충돌 방지와 재시도 로직"),
        ("DrawingData(LectureId, MemberId)", "강의별 사용자 필기 snapshot 중복 저장 방지"),
        ("Foreign key + cascade", "강의/회원 삭제 시 orphan drawing data 방지"),
        ("JSONB", "필기 요소를 페이지/사용자 단위로 통째로 로드하는 접근 패턴에 맞춤"),
        ("EXPLAIN script", "주요 조회가 index scan을 타는지 확인하는 문서와 SQL 제공"),
    ]
    y = draw_table(
        c,
        MARGIN_X,
        y,
        CONTENT_W,
        [(a, "적용", b) for a, b in rows],
        [150, 60, CONTENT_W - 210],
        ("DB 항목", "상태", "설명"),
    )
    y = section_header(c, MARGIN_X, y, "저장 흐름")
    y = bullet_list(c, MARGIN_X, y, [
        "실시간 편집 중에는 Redis/InMemory 저장소에서 최신 drawing state를 조회합니다.",
        "교수자 연결 종료 시 현재 drawing state를 PostgreSQL DrawingData에 snapshot으로 저장합니다.",
        "저장 경로는 ExecuteUpdateAsync()를 먼저 시도하고, insert race는 unique conflict 후 update로 회복합니다.",
        "Controller와 Hub는 인증된 claim 기준으로 lecture 접근 권한을 확인합니다.",
    ], CONTENT_W, 9.2)
    y -= 8
    card(c, MARGIN_X, y, CONTENT_W, 78, "검증 결과",
         "2026.06.25 기준 CanvaSync solution build는 0 warning / 0 error로 통과했습니다. DB 개선은 migration과 model snapshot에 반영되어 있습니다.",
         GREEN, SOFT_GREEN)


def fit_and_plan(c: canvas.Canvas) -> None:
    new_page(c, "Fit")
    y = title(c, TOP, "7. 채용 공고 기준 적합도와 보완 계획", "MLB 라이벌 서버 프로그래머 공고의 요구사항을 프로젝트 증거와 연결했습니다.")
    rows = [
        ("모바일 게임서버", "핵심 증거", "PolRob: 모바일 클라이언트 + 인게임 서버 + TCP/UDP transport"),
        ("컨텐츠 데이터 설계", "보완 증거", "CanvaSync DB 설계로 SQL/RDB 설계 역량 제시. 게임 master data는 추가 보완 가능"),
        ("라이브 유지보수/자동화", "개선 기록", "부하 테스트/문서/배포 경험을 통해 문제 측정과 개선 과정을 제시"),
        ("DB/쿼리/튜닝", "보완 증거", "PostgreSQL index, FK, JSONB, EXPLAIN 문서. MySQL은 동일 SQL/RDB 관점으로 설명"),
        ("네트워크/비동기", "핵심 증거", "SignalR, TCP/UDP, Channel<T>, room loop, rate limit"),
        ("클라우드", "배포 경험", "CanvaSync Azure 전시 배포. 상용 대규모 운영 경험과는 구분해서 설명"),
        ("PHP/Golang", "학습 예정", "주력은 C#/.NET. 지원 스택 적응을 위해 Go/PHP 소규모 API 과제 추가 예정"),
        ("Docker/Linux", "보완 예정", "로컬 인프라 명령 경험을 서비스 컨테이너 실행 문서로 확장 예정"),
    ]
    y = draw_table(c, MARGIN_X, y, CONTENT_W, rows, [110, 60, CONTENT_W - 170], ("공고 키워드", "증거 수준", "프로젝트 근거"))
    y = section_header(c, MARGIN_X, y, "지원 전 마지막 보완")
    bullet_list(c, MARGIN_X, y, [
        "PolRob 문서에서 미완성 표현(TBD 등)을 정리하고, 최종 combined benchmark를 한 번 더 재측정합니다.",
        "CanvaSync EXPLAIN 결과를 실제 캡처해 DB 튜닝 문서에 붙입니다.",
        "Docker/Linux 실행 경로와 Go 또는 PHP 소규모 도구를 하나 추가하면 공고 스택 미스매치를 줄일 수 있습니다.",
    ], CONTENT_W, 9.2)


def appendix(c: canvas.Canvas) -> None:
    new_page(c, "Appendix")
    y = title(c, TOP, "8. 검증 스냅샷과 면접 포인트", "포트폴리오에서 말할 때는 구현 사실과 검증 범위를 분리해서 설명합니다.")
    y = section_header(c, MARGIN_X, y, "빌드 검증")
    y = bullet_list(c, MARGIN_X, y, [
        "PolRob Server: dotnet build polrob.Server.csproj --no-restore -> 0 warning / 0 error",
        "PolRob Bot Test: dotnet build polrob.Test.csproj --no-restore -> 0 warning / 0 error",
        "CanvaSync Solution: dotnet build canvasync.sln --no-restore -> 0 warning / 0 error",
    ], CONTENT_W, 9.2)
    y -= 6
    y = section_header(c, MARGIN_X, y, "면접에서 강조할 이야기")
    y = bullet_list(c, MARGIN_X, y, [
        "PolRob: 왜 TCP와 UDP를 나눴는지, 왜 클라이언트 좌표를 신뢰하지 않는지, 방별 루프가 어떤 동시성 문제를 줄이는지 설명합니다.",
        "PolRob: 최적화 수치는 처리량 자랑이 아니라 bottleneck을 분리해 측정하고 개선한 경험으로 설명합니다.",
        "CanvaSync: 왜 PDF 원본은 Blob Storage로 빼고, 필기 snapshot은 PostgreSQL JSONB로 저장했는지 설명합니다.",
        "CanvaSync: unique index, FK, 권한 검증, query-plan 확인을 통해 DB 무결성과 조회 패턴을 의식했다고 설명합니다.",
    ], CONTENT_W, 9.2)
    y -= 8
    card(c, MARGIN_X, y, CONTENT_W, 96, "검증 범위와 태도",
         "두 프로젝트는 상용 대규모 서비스 운영 경험이 아니라 개인/졸업작품 기반 검증입니다. 따라서 실제 서비스 규모를 과장하지 않고, 코드와 측정 결과로 증명 가능한 범위를 명확히 제시합니다. 면접에서는 설계 판단, 디버깅 과정, 성능 측정 방식, 보안 검토 기준을 본인의 언어로 설명하는 데 집중합니다.",
         RED, SOFT_RED)


def main() -> None:
    OUT.parent.mkdir(parents=True, exist_ok=True)
    c = canvas.Canvas(str(OUT), pagesize=A4)
    c.setTitle("Game Server Portfolio - PolRob / CanvaSync")
    c.setAuthor("Shinwoo Seo")
    cover(c)
    summary_page(c)
    polrob_overview(c)
    polrob_cases(c)
    polrob_performance(c)
    canvasync_overview(c)
    canvasync_db(c)
    fit_and_plan(c)
    appendix(c)
    c.save()
    print(OUT)


if __name__ == "__main__":
    main()
