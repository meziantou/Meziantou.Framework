Content-Type: multipart/mixed; boundary="boundary"

preamble

--boundary
Content-Type: text/plain

first part

--boundary    
Content-Type: text/plain

from the rfc:
================================
encapsulation := delimiter transport-padding
                      CRLF body-part
================================
and also
================================
"Composers MUST NOT generate non-zero length transport
padding, but receivers MUST be able to handle padding
added by message transports."
================================

second part have a start boundary ending with spaces
and also have a boundary not at the beginning
 --boundary
... that should be ignored

also a boundary with more data (a tab) shoud be ignored
 --boundary	
 
end of part

--boundary--
epilouge