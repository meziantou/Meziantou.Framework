'use strict';

// The WebUSB Testing API (https://wicg.github.io/webusb/test/) of the runner: navigator.usb.test, on the fake devices of
// WptUsbProvider (Runner/WptUsb.cs), as the polyfill of Chromium (/resources/chromium/webusb-test.js) on its Mojo service.
// The devices are shared by the realms of the test (the windows, the frames and the workers), so attachToContext() has
// nothing to do.
(() => {
  const host = {
    create: self.__wpt_usb_create,
    connect: self.__wpt_usb_connect,
    disconnect: self.__wpt_usb_disconnect,
    fail: self.__wpt_usb_fail,
    stall: self.__wpt_usb_stall,
    status: self.__wpt_usb_status,
    reset: self.__wpt_usb_reset,
    setRequestHandler: self.__wpt_usb_set_request_handler,
    setCloseHandler: self.__wpt_usb_set_close_handler,
  };
  for (const name of ['create', 'connect', 'disconnect', 'fail', 'stall', 'status', 'reset', 'set_request_handler', 'set_close_handler']) {
    delete self['__wpt_usb_' + name];
  }

  if (!self.navigator || !navigator.usb)
    return;

  const ids = new WeakMap();
  const fakeDevices = new Map();
  const transferStatuses = ['transfer-error', 'permission-denied', 'timeout', 'cancelled', 'disconnect', 'stall', 'babble'];

  class FakeUSBDevice {
    constructor(id) {
      ids.set(this, id);
      this.onclose = null;
    }

    failNextOperation(operation) {
      host.fail(ids.get(this), String(operation));
    }

    stallNextOperation(operation) {
      host.stall(ids.get(this), String(operation));
    }

    setNextTransferStatus(status) {
      if (!transferStatuses.includes(status))
        throw new Error('unknown transfer status');
      host.status(ids.get(this), status);
    }

    disconnect() {
      const id = ids.get(this);
      setTimeout(() => host.disconnect(id), 0);
    }
  }

  class USBDeviceRequestEvent {
    #respond;

    constructor(filters, exclusionFilters, respond) {
      this.filters = filters;
      this.exclusionFilters = exclusionFilters;
      this.#respond = respond;
    }

    respondWith(value) {
      // Wait until |value| resolves (if it is a Promise). This function returns no value.
      Promise.resolve(value).then(
          fakeDevice => this.#respond(ids.has(fakeDevice) ? ids.get(fakeDevice) : -1),
          () => this.#respond(-1));
    }
  }

  let initialized = false;
  const checkInitialized = name => {
    if (!initialized)
      throw new Error(`Call initialize() before ${name}().`);
  };

  const test = {
    onrequestdevice: undefined,

    async initialize() {
      // As the polyfill of Chromium, a call to getDevices() establishes that everything is set up
      await navigator.usb.getDevices();
      initialized = true;
    },

    // The polyfill of Chromium waits for the ReadyForAttachment message of a frame (webusb-child-test.js): the frame is loaded
    async attachToContext(context) {
      checkInitialized('attachToContext');
      if (typeof HTMLIFrameElement !== 'undefined' && context instanceof HTMLIFrameElement) {
        const document = context.contentDocument;
        if (!document || document.readyState !== 'complete' || context.contentWindow.location.href === 'about:blank')
          await new Promise(resolve => context.addEventListener('load', resolve, { once: true }));
      }
    },

    addFakeDevice(deviceInit) {
      checkInitialized('addFakeDevice');
      const fakeDevice = new FakeUSBDevice(host.create(JSON.stringify(deviceInit)));
      fakeDevices.set(ids.get(fakeDevice), fakeDevice);

      // As in the polyfill of Chromium, the device is added by a task: the tests do not rely on it being available at once
      setTimeout(() => host.connect(ids.get(fakeDevice)), 0);
      return fakeDevice;
    },

    reset() {
      checkInitialized('reset');
      return new Promise(resolve => {
        setTimeout(() => {
          host.reset();
          resolve();
        }, 0);
      });
    },
  };

  host.setCloseHandler(id => {
    const fakeDevice = fakeDevices.get(id);
    if (fakeDevice && typeof fakeDevice.onclose === 'function')
      fakeDevice.onclose();
  });

  host.setRequestHandler((filters, exclusionFilters, respond) => {
    if (typeof test.onrequestdevice === 'function') {
      test.onrequestdevice(new USBDeviceRequestEvent(JSON.parse(filters), JSON.parse(exclusionFilters), respond));
    } else {
      respond(-1);
    }
  });

  Object.defineProperty(navigator.usb, 'test', { value: test, writable: true, enumerable: false, configurable: true });
})();
