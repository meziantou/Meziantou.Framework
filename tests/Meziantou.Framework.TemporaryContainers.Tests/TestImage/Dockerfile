FROM busybox:1.38@sha256:fd7dc98638c8e305f4dc34e979f1c0fdfdcaeb0fbf8fcff77ae834b6da3d7e6e

RUN mkdir -p /www && printf 'hello from container' > /www/index.html

EXPOSE 8080

CMD ["sh", "-c", "echo SERVER READY; exec httpd -f -p 8080 -h /www"]
