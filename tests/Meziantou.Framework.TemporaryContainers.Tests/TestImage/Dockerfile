FROM busybox:1.38@sha256:a46257e06c82ad85242d0d38077d0a52d9896389ca60a1a59d3463db586e5fa4

RUN mkdir -p /www && printf 'hello from container' > /www/index.html

EXPOSE 8080

CMD ["sh", "-c", "echo SERVER READY; exec httpd -f -p 8080 -h /www"]
