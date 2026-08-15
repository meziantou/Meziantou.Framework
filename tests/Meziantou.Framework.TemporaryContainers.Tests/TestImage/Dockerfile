FROM busybox:1.38@sha256:dc2d74b28e4cf8984fa52af1f39bc7c3d9c73760b41a74d629f5d11b1ab28616

RUN mkdir -p /www && printf 'hello from container' > /www/index.html

EXPOSE 8080

CMD ["sh", "-c", "echo SERVER READY; exec httpd -f -p 8080 -h /www"]
