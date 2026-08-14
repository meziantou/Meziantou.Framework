FROM busybox:1.37@sha256:9db7b59979c38555a39def84a31fb98b5296952f9e3afd4f6f11f05b07adfab0

RUN mkdir -p /www && printf 'hello from container' > /www/index.html

EXPOSE 8080

CMD ["sh", "-c", "echo SERVER READY; exec httpd -f -p 8080 -h /www"]
