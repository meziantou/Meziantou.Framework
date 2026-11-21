// Writes the test files of tests/Meziantou.Framework.Mail.Tests/TestData/Mailbox: mbox files in the four variants, an .emlx file and a Thunderbird summary (.msf)
// Usage: node make-mailbox.js <directory>
const fs = require('fs');
const path = require('path');
const dir = process.argv[2];
fs.mkdirSync(dir, { recursive: true });

// The messages, with lines that start with "From " or ">From " in their bodies
const messages = [
    {
        sender: 'alice@example.com',
        date: 'Tue Jul  1 08:52:37 2025',
        text: `From: Alice <alice@example.com>
To: Bob <bob@example.com>
Subject: First
Date: Tue, 1 Jul 2025 10:52:37 +0200
Message-ID: <first@example.com>

Hello Bob,
From the start of a line.
>From a quoted line.
>>From a line quoted twice.
The word From in the middle: no escape.
`,
    },
    {
        sender: 'bob@example.com',
        date: 'Wed Jul  2 08:00:00 2025',
        text: `From: Bob <bob@example.com>
To: Alice <alice@example.com>
Subject: =?UTF-8?B?Q2Fmw6k=?=
Date: Wed, 2 Jul 2025 08:00:00 +0000
Message-ID: <second@example.com>
MIME-Version: 1.0
Content-Type: multipart/mixed; boundary="b1"

--b1
Content-Type: text/plain; charset=utf-8

Café.

From here, the attachment.
--b1
Content-Type: application/octet-stream; name="data.bin"
Content-Transfer-Encoding: base64

AAECAwQFBgcICQ==
--b1--
`,
    },
    {
        sender: 'carol@example.com',
        date: 'Thu Jul  3 23:59:59 2025',
        text: `From: Carol <carol@example.com>
To: Bob <bob@example.com>
Subject: Last, without a final line break
Date: Thu, 3 Jul 2025 23:59:59 +0000
Message-ID: <third@example.com>

The last line.`,
    },
];

function escape(body, format) {
    return body.split('\n').map(line => {
        if (format === 'mboxrd' && /^>*From /.test(line)) return '>' + line;
        if ((format === 'mboxo' || format === 'mboxcl') && /^From /.test(line)) return '>' + line;
        return line;
    }).join('\n');
}

function mbox(format, crlf) {
    let result = '';
    for (const message of messages) {
        let text = message.text.endsWith('\n') ? message.text : message.text + '\n';
        const split = text.indexOf('\n\n');
        let headers = text.slice(0, split + 1);
        let body = escape(text.slice(split + 2), format);
        if (format === 'mboxcl' || format === 'mboxcl2') {
            headers += 'Content-Length: ' + Buffer.byteLength(body, 'utf8') + '\n';
        }

        result += `From ${message.sender} ${message.date}\n${headers}\n${body}\n`;
    }

    return crlf ? result.replace(/\n/g, '\r\n') : result;
}

for (const format of ['mboxo', 'mboxrd', 'mboxcl', 'mboxcl2']) {
    fs.writeFileSync(path.join(dir, format + '.mbox'), mbox(format, false));
}

fs.writeFileSync(path.join(dir, 'mboxrd-crlf.mbox'), mbox('mboxrd', true));

// An .emlx file of Apple Mail: the size of the message, the message, then a property list
const emlxMessage = messages[0].text;
const plist = `<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>date-last-viewed</key>
	<integer>1751360000</integer>
	<key>date-received</key>
	<integer>1751359957</integer>
	<key>flags</key>
	<integer>8590195713</integer>
	<key>original-mailbox</key>
	<string>imap://bob@imap.example.com/INBOX</string>
	<key>remote-id</key>
	<string>4242</string>
	<key>subject</key>
	<string>First</string>
</dict>
</plist>
`;
fs.writeFileSync(path.join(dir, 'message.emlx'), `${Buffer.byteLength(emlxMessage, 'utf8')}\n${emlxMessage}${plist}`);

// A Thunderbird summary: the dictionary of the columns, a dictionary of values, the table of the messages,
// a committed group that updates a row, an aborted group, a literal on two lines and escaped characters
const msf = `// <!-- <mdb:mork:z v="1.4"/> -->
< <(a=c)> // (f=iso-8859-1)
  (B8=Ns:msg:db:row:scope:dbfolderinfo:all)(B9=ns:msg:db:table:kind:dbfolderinfo)
  (BA=numMsgs)(80=ns:msg:db:row:scope:msgs:all)(81=subject)(82=sender)
  (83=message-id)(84=references)(85=recipients)(86=date)(87=size)(88=flags)
  (89=priority)(8A=label)(8B=statusOfset)(8C=numLines)(8D=ccList)
  (8E=msgThreadId)(8F=threadId)(90=threadFlags)(91=threadNewestMsgDate)
  (92=children)(93=unreadChildren)(94=threadSubject)(95=storeToken)
  (96=dateReceived)(97=ns:msg:db:table:kind:msgs)>

<(A0=First)(A1=Alice <alice@example.com>)(A2=first@example.com)
  (A3=Bob <bob@example.com>)(A4=6863a1d5)(A5=1)(A6=0)>

{1:^80 {(k^97:c)(s=9)}
  [1(^81^A0)(^82^A1)(^83^A2)(^85^A3)(^86^A4)(^88=10001)(^87=1a5)(^8C=5)(^95=0)]
  [2(^81=Caf$C3$A9)(^82=Bob <bob@example.com>)(^83=second@example.com)(^85=Alice <alice@example.com>)(^86=6864e700)(^88=10000)(^87=1f0)(^95=421)]
  [3(^81=Last, without a final line \\
break)(^82=Carol <carol@example.com>)(^83=third@example.com)(^86=68671aff)(^88=0)(^95=999)(^8D=Dave \\(work\\) <dave@example.com>)]}

[1:^B8(^BA=3)]

@$\${1{@
[1:^80(^88=10011)]
@$$}1}@

@$\${2{@
[1:^80(^88=ffff)]
@$$}~~}@
`;
fs.writeFileSync(path.join(dir, 'summary.msf'), msf);
