Content-Type: multipart/mixed; boundary="boundary"

--boundary This should be ignored Content-Type: message/rfc822
Content-Type: text/plain

first part

--boundary-- This should be ignored at all and not part of the epilogue.

From the RFC about ending boundary:

===================================================================
NOTE TO IMPLEMENTORS:  Boundary string comparisons must compare the
boundary value with the beginning of each candidate line.  An exact
match of the entire candidate line is not required; it is sufficient
that the boundary appear in its entirety following the CRLF.
===================================================================

--boundary--

The above boundary should be part of the epilogue, too.