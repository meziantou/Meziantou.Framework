// A CommonJS module: its exports are the properties of module.exports
exports.total = items => items.reduce((sum, item) => sum + item.price * item.quantity, 0);
