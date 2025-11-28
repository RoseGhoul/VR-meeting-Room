import pygame
import threading
import socket
import cv2
import pickle
import struct
import mss
import numpy as np
import sys
import time

# Streaming flag
streaming = False

# TCP port for Unity clients to connect
STREAM_PORT = 9999

def get_local_ip():
    """Find the current local IP automatically."""
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        s.connect(("8.8.8.8", 80))
        ip = s.getsockname()[0]
    except Exception:
        ip = "127.0.0.1"
    finally:
        s.close()
    return ip

def handle_client(conn, addr):
    """Send screen frames to a connected Unity client."""
    print(f"Client connected: {addr}")
    with mss.mss() as sct:
        monitor = sct.monitors[1]
        while streaming:
            img = np.array(sct.grab(monitor))
            frame = cv2.cvtColor(img, cv2.COLOR_BGRA2BGR)

            # Encode as JPEG
            _, encoded = cv2.imencode('.jpg', frame, [cv2.IMWRITE_JPEG_QUALITY, 70])
            if not _:
                print("Encoding failed for this frame")
            data = encoded.tobytes()
            message = struct.pack("Q", len(data)) + data

            try:
                conn.sendall(message)
            except Exception:
                print(f"Client {addr} disconnected")
                break
    conn.close()

def start_server(port):
    """Start TCP server to accept Unity clients."""
    global streaming
    streaming = True
    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.bind(('', port))  # Listen on all interfaces
    server.listen()
    print(f"Python screen server listening on port {port}")

    while streaming:
        try:
            conn, addr = server.accept()
            threading.Thread(target=handle_client, args=(conn, addr), daemon=True).start()
        except Exception as e:
            print("Server accept error:", e)
            break
    server.close()
    print("Server stopped")

def main():
    global streaming

    pygame.init()
    screen = pygame.display.set_mode((550, 300))
    pygame.display.set_caption("PC Screen Streamer (Server Mode)")

    font = pygame.font.SysFont("Arial", 22)
    clock = pygame.time.Clock()

    local_ip = get_local_ip()

    # Start TCP server
    threading.Thread(target=start_server, args=(STREAM_PORT,), daemon=True).start()

    running = True
    while running:
        screen.fill((30, 30, 30))
        screen.blit(font.render(f"Your IP: {local_ip}", True, (255, 255, 0)), (20, 20))
        status = "Streaming..." if streaming else "Stopped"
        screen.blit(font.render(f"Status: {status}", True, (255, 255, 0)), (200, 250))
        screen.blit(font.render("Unity Client must connect to this IP manually.", True, (255, 255, 255)), (20, 60))
        pygame.display.flip()

        for event in pygame.event.get():
            if event.type == pygame.QUIT:
                streaming = False
                running = False

        clock.tick(30)

    pygame.quit()
    sys.exit()

if __name__ == "__main__":
    main()
