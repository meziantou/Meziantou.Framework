// Writes the MIME test messages of tests/Meziantou.Framework.Mail.Tests/TestData/Mime
const fs = require('fs');
const dir = process.argv[2];
fs.mkdirSync(dir, { recursive: true });
const crlf = s => s.replace(/\r?\n/g, '\r\n');
const b64 = (s, enc = 'utf8') => Buffer.from(s, enc).toString('base64').replace(/(.{76})/g, '$1\r\n');
const write = (name, text, lf = false) => fs.writeFileSync(dir + '/' + name, lf ? text.replace(/\r\n/g, '\n') : crlf(text));
const png = Buffer.from('89504E470D0A1A0A0000000D49484452000000010000000108060000001F15C4890000000D4944415478DA6364F8CF00000301010018DD8DB00000000049454E44AE426082', 'hex');

write('simple.eml', `From: Alice <alice@example.com>
To: Bob <bob@example.com>
Subject: Hello
Date: Tue, 1 Jul 2025 10:52:37 +0200
Message-ID: <simple-1@example.com>

Hello Bob,
This is a simple message.
`);

write('encoded-words.eml', `From: =?UTF-8?B?w4lsb2RpZQ==?= <elodie@example.com>
To: "=?ISO-8859-1?Q?Andr=E9?=" <andre@example.com>, =?utf-8?q?J=C3=BCrgen?= <juergen@example.com>
Subject: =?UTF-8?B?Q2Fmw6k=?= =?UTF-8?Q?_et_cr=C3=A8me?= =?ISO-8859-15?Q?_=A4uro?=
Date: Wed, 2 Jul 2025 08:00:00 +0000
Message-ID: <encoded-1@example.com>
Content-Type: text/plain; charset=utf-8
Content-Transfer-Encoding: 8bit

Café crème.
`);

write('quoted-printable.eml', `From: alice@example.com
To: bob@example.com
Subject: Quoted-printable
Date: 3 Jul 2025 12:00 GMT
MIME-Version: 1.0
Content-Type: text/plain; charset=iso-8859-1
Content-Transfer-Encoding: quoted-printable

Voil=E0 un texte en fran=E7ais avec une tr=E8s longue ligne qui est coup=E9e =
par un saut de ligne doux.=20
Fin=3D.
`);

write('alternative.eml', `From: Alice <alice@example.com>
To: Bob <bob@example.com>, Carol <carol@example.com>
Cc: Dave <dave@example.com>
Subject: Alternative bodies
Date: Thu, 3 Jul 2025 09:30:00 -0400
Message-ID: <alternative-1@example.com>
In-Reply-To: <simple-1@example.com>
References: <root@example.com> <simple-1@example.com>
MIME-Version: 1.0
Content-Type: multipart/alternative; boundary="alt-boundary"

This is the preamble.

--alt-boundary
Content-Type: text/plain; charset=utf-8

Plain text.
--alt-boundary
Content-Type: text/html; charset=utf-8

<p>HTML <b>text</b>.</p>
--alt-boundary--
This is the epilogue.
`);

write('related-inline.eml', `From: Alice <alice@example.com>
To: Bob <bob@example.com>
Subject: Inline image
Date: Fri, 4 Jul 2025 14:00:00 +0100
MIME-Version: 1.0
Content-Type: multipart/related; boundary="rel"; type="text/html"

--rel
Content-Type: text/html; charset=us-ascii

<img src="cid:image1@example.com">
--rel
Content-Type: image/png; name="dot.png"
Content-Transfer-Encoding: base64
Content-ID: <image1@example.com>
Content-Disposition: inline; filename="dot.png"

${b64(png.toString('latin1'), 'latin1')}
--rel--
`);

write('attachments.eml', `From: Alice <alice@example.com>
To: Bob <bob@example.com>
Subject: Attachments
Date: Sat, 5 Jul 2025 16:00:00 +0000
MIME-Version: 1.0
Content-Type: multipart/mixed; boundary="mixed"

--mixed
Content-Type: text/plain

See the attachments.
--mixed
Content-Type: application/octet-stream
Content-Transfer-Encoding: base64
Content-Disposition: attachment;
 filename*0*=utf-8''r%C3%A9sum%C3%A9%20tr%C3%A8s%20;
 filename*1*=long%20nom.txt

${b64('Contenu du fichier\n')}
--mixed
Content-Type: text/csv; name="=?UTF-8?B?ZGF0w6EuY3N2?="
Content-Disposition: attachment

a,b
1,2
--mixed
Content-Type: application/pdf; name="report.pdf"
Content-Transfer-Encoding: base64

${b64('%PDF-1.4 fake\n')}
--mixed--
`);

write('embedded-message.eml', `From: Alice <alice@example.com>
To: Bob <bob@example.com>
Subject: Fwd: Hello
Date: Sun, 6 Jul 2025 10:00:00 +0000
MIME-Version: 1.0
Content-Type: multipart/mixed; boundary="outer"

--outer
Content-Type: text/plain

Forwarded message below.
--outer
Content-Type: message/rfc822
Content-Disposition: attachment; filename="hello.eml"

From: Carol <carol@example.com>
To: Alice <alice@example.com>
Subject: Hello
Date: Sat, 5 Jul 2025 09:00:00 +0000
MIME-Version: 1.0
Content-Type: multipart/mixed; boundary="inner"

--inner
Content-Type: text/plain

Inner text.
--inner
Content-Type: text/plain; name="inner.txt"
Content-Disposition: attachment; filename="inner.txt"

Inner attachment.
--inner--
--outer--
`);

write('nested.eml', `From: Alice <alice@example.com>
To: Bob <bob@example.com>
Subject: Nested multiparts
Date: Mon, 7 Jul 2025 10:00:00 +0000
MIME-Version: 1.0
Content-Type: multipart/mixed; boundary="a"

--a
Content-Type: multipart/alternative; boundary="b"

--b
Content-Type: text/plain

Nested plain.
--b
Content-Type: multipart/related; boundary="c"

--c
Content-Type: text/html

<p>Nested html</p>
--c--
--b--
--a
Content-Type: application/zip
Content-Disposition: attachment; filename=archive.zip
Content-Transfer-Encoding: base64

UEsFBgAAAAAAAAAAAAAAAAAAAAAAAA==
--a--
`);

write('lf-line-endings.eml', `From: Alice <alice@example.com>
To: Bob <bob@example.com>
Subject: LF line endings
Date: Tue, 8 Jul 2025 10:00:00 +0000
MIME-Version: 1.0
Content-Type: multipart/mixed; boundary=simple

--simple
Content-Type: text/plain

Lines end with LF only.
--simple
Content-Type: text/plain; name=notes.txt
Content-Disposition: attachment; filename=notes.txt

Notes.
--simple--
`, true);

write('unclosed-multipart.eml', `From: Alice <alice@example.com>
To: Bob <bob@example.com>
Subject: Missing close delimiter
Date: Wed, 9 Jul 2025 10:00:00 +0000
MIME-Version: 1.0
Content-Type: multipart/mixed; boundary="open"

--open
Content-Type: text/plain

The multipart is never closed.
`);

write('groups-and-comments.eml', `From: "Smith, John" (the boss) <john.smith@example.com>
To: Team: alice@example.com, Bob <bob@example.com>;, carol@example.com (Carol)
Cc: undisclosed-recipients:;
Reply-To: <replies@example.com>
Subject: Groups
Date: Thu, 10 Jul 25 10:00:00 EST
Message-ID: <groups@example.com>

Body.
`);

write('utf8-headers.eml', `From: René <rene@example.com>
To: Jürgen <juergen@example.com>
Subject: Réunion à Zürich \u{1F600}
Date: Fri, 11 Jul 2025 10:00:00 +0000
MIME-Version: 1.0
Content-Type: text/plain; charset=utf-8
Content-Transfer-Encoding: 8bit

Texte en UTF-8 : éèà \u{1F600}.
`);

write('digest.eml', `From: list@example.com
To: subscriber@example.com
Subject: Digest
Date: Sat, 12 Jul 2025 10:00:00 +0000
MIME-Version: 1.0
Content-Type: multipart/digest; boundary="d"

--d

From: one@example.com
Subject: First

First message.
--d

From: two@example.com
Subject: Second

Second message.
--d--
`);

write('uuencode.eml', `From: old@example.com
To: bob@example.com
Subject: uuencode
Date: Sun, 13 Jul 2025 10:00:00 +0000
MIME-Version: 1.0
Content-Type: multipart/mixed; boundary="u"

--u
Content-Type: text/plain

Old client.
--u
Content-Type: application/octet-stream; name="hello.txt"
Content-Transfer-Encoding: x-uuencode
Content-Disposition: attachment; filename="hello.txt"

begin 644 hello.txt
,:&5L;&\\@=V]R;&0*
\`
end
--u--
`);

write('malformed.eml', `From: no-name
To:
Subject:no space after colon
Date: not a date
Content-Type: multipart/mixed
Content-Transfer-Encoding: base64

This multipart has no boundary.
`);

write('latin1-body.eml', `From: alice@example.com
To: bob@example.com
Subject: Latin-1 body
Date: Mon, 14 Jul 2025 10:00:00 +0000
MIME-Version: 1.0
Content-Type: text/plain; charset="windows-1252"
Content-Transfer-Encoding: base64

${b64('Café € 100\n', 'latin1').replace('\r\n', '\r\n')}
`);

// windows-1252: the euro sign is 0x80
const cp1252 = Buffer.from([0x43, 0x61, 0x66, 0xE9, 0x20, 0x80, 0x20, 0x31, 0x30, 0x30, 0x0A]).toString('base64');
fs.writeFileSync(dir + '/latin1-body.eml', crlf(fs.readFileSync(dir + '/latin1-body.eml', 'utf8').replace(/Content-Transfer-Encoding: base64\r\n\r\n[\s\S]*$/, 'Content-Transfer-Encoding: base64\r\n\r\n' + cp1252 + '\r\n')));
console.log(fs.readdirSync(dir).join(' '));
