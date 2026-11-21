# Offline address book test files

`ms-oxoab-example.oab` is the example of an uncompressed version 4 Full Details file of [MS-OXOAB] section 3.3 ("Full OAB
Version 4 Details File"), revision 16.0 of 2025-05-20: the bytes the specification lists, in their order. The Open
Specifications documentation lets implementers copy it to develop implementations of the format. Its names and addresses
are the fictitious ones of the specification.

`OabFileTests` reads it and checks the values the specification lists after it, and checks that the writer writes it back
byte for byte, with its checksum.

[MS-OXOAB]: https://learn.microsoft.com/openspecs/exchange_server_protocols/ms-oxoab
