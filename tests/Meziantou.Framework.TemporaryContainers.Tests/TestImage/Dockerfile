FROM busybox:1.37

RUN mkdir -p /www && printf 'hello from container' > /www/index.html

EXPOSE 8080

CMD ["sh", "-c", "echo SERVER READY; exec httpd -f -p 8080 -h /www"]
