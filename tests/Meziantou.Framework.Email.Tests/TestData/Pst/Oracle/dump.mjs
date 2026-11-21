// Dumps the content of PST/OST files as JSON with pst-extractor (https://github.com/epfromer/pst-extractor, MIT).
// Usage: npm install pst-extractor@1.12.0, then node dump.mjs file1.pst file2.ost ... > expected.json
import { createHash } from 'node:crypto';
import { basename } from 'node:path';
import { execFileSync } from 'node:child_process';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import pst from 'pst-extractor';

const Long = createRequire(import.meta.url)('long');

// pst-extractor compares the keys of the B-trees of ANSI files with '>' on Long objects, which compares their strings: a Long
// that converts to a number makes it search the B-trees that have more than one level
Long.prototype.valueOf = function () {
    return this.toNumber();
};

const { PSTFile, PSTFolder } = pst;

const sha256 = data => createHash('sha256').update(data).digest('hex');
const text = value => (value === undefined || value === null || value === '' ? null : value);
const date = value => (value ? value.toISOString() : null);
const textHash = value => (value ? sha256(Buffer.from(value, 'utf8')) : null);

function readStream(stream, size) {
    if (!stream) {
        return null;
    }

    const chunks = [];
    const buffer = Buffer.alloc(8176);
    let read;
    while ((read = stream.readBlock(buffer)) > 0) {
        chunks.push(Buffer.from(buffer.subarray(0, read)));
    }

    return Buffer.concat(chunks);
}

function dumpItem(item, depth) {
    const result = {
        messageClass: text(item.messageClass),
        subject: text(item.subject),
        senderName: text(item.senderName),
        senderEmailAddress: text(item.senderEmailAddress),
        displayTo: text(item.displayTo),
        importance: item.importance,
        clientSubmitTime: date(item.clientSubmitTime),
        messageDeliveryTime: date(item.messageDeliveryTime),
        internetMessageId: text(item.internetMessageId),
        bodySha256: textHash(item.body),
        bodyHtmlSha256: textHash(item.bodyHTML),
        recipients: [],
        attachments: [],
    };

    for (let i = 0; i < item.numberOfRecipients; i++) {
        const recipient = item.getRecipient(i);
        result.recipients.push({
            displayName: text(recipient.displayName),
            emailAddress: text(recipient.emailAddress),
            recipientType: recipient.recipientType,
        });
    }

    for (let i = 0; i < item.numberOfAttachments; i++) {
        const attachment = item.getAttachment(i);
        const entry = {
            filename: text(attachment.filename),
            longFilename: text(attachment.longFilename),
            mimeTag: text(attachment.mimeTag),
            attachMethod: attachment.attachMethod,
        };
        if (attachment.attachMethod === 5) {
            const embedded = attachment.embeddedPSTMessage;
            entry.embeddedMessage = embedded && depth < 5 ? dumpItem(embedded, depth + 1) : null;
        } else {
            const content = readStream(attachment.fileInputStream);
            entry.contentSha256 = content ? sha256(content) : null;
            entry.contentLength = content ? content.length : null;
        }

        result.attachments.push(entry);
    }

    switch (item.constructor.name) {
        case 'PSTContact':
            result.contact = {
                givenName: text(item.givenName),
                surname: text(item.surname),
                companyName: text(item.companyName),
                email1EmailAddress: text(item.email1EmailAddress),
                businessTelephoneNumber: text(item.businessTelephoneNumber),
                mobileTelephoneNumber: text(item.mobileTelephoneNumber),
            };
            break;
        case 'PSTAppointment':
            result.appointment = {
                startTime: date(item.startTime),
                endTime: date(item.endTime),
                location: text(item.location),
                isRecurring: item.isRecurring,
                busyStatus: item.busyStatus,
            };
            break;
        case 'PSTTask':
            result.task = {
                taskStatus: item.taskStatus,
                percentComplete: item.percentComplete,
                taskOwner: text(item.taskOwner),
            };
            break;
    }

    return result;
}

function dumpFolder(folder) {
    const result = {
        displayName: text(folder.displayName),
        containerClass: text(folder.containerClass),
        contentCount: folder.contentCount,
        unreadCount: folder.unreadCount,
        items: [],
        folders: [],
    };

    if (folder.contentCount > 0) {
        let item = folder.getNextChild();
        while (item) {
            result.items.push(dumpItem(item, 0));
            item = folder.getNextChild();
        }
    }

    if (folder.hasSubfolders) {
        for (const child of folder.getSubFolders()) {
            result.folders.push(dumpFolder(child));
        }
    }

    return result;
}

// pst-extractor keeps the name-to-ID map in a static field: each file is dumped by its own process
const files = process.argv.slice(2);
if (files.length > 1) {
    const output = {};
    for (const path of files) {
        Object.assign(output, JSON.parse(execFileSync(process.execPath, [fileURLToPath(import.meta.url), path], { maxBuffer: 1024 * 1024 * 1024, encoding: 'utf8' })));
    }

    process.stdout.write(JSON.stringify(output, null, 2) + '\n');
    process.exit(0);
}

const output = {};
for (const path of files) {
    const file = new PSTFile(path);
    output[basename(path)] = {
        storeDisplayName: text(file.getMessageStore().displayName),
        rootFolder: dumpFolder(file.getRootFolder()),
    };
    file.close();
}

process.stdout.write(JSON.stringify(output, null, 2) + '\n');
