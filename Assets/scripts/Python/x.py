import socket
s = socket.socket()
s.settimeout(3)
s.connect(('172.16.211.23', 9999))
        