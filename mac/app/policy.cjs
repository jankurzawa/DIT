const path = require('node:path');
const AUDIO = new Set(['.wav','.bwf','.rf64','.mp3','.flac','.aac','.aif','.aiff','.m4a']);
function validateApi(method, route) {
  if (!['GET','POST','PUT'].includes(method) || typeof route !== 'string' ||
      !/^\/(health|projects(?:\/[^?#]*)?|login|session\/(activity|lock|lock-all|leave|enter))$/.test(route) ||
      /[\x00-\x20\\]|\.\.|%2f|%5c/i.test(route)) throw Error('Nieprawidłowe żądanie.');
}
function validateAudio(files) {
  if (!Array.isArray(files) || files.some(file => typeof file !== 'string' || !AUDIO.has(path.extname(file).toLowerCase())))
    throw Error('Zakładka Audio przyjmuje wyłącznie pliki audio.');
  return files;
}
module.exports = {validateApi, validateAudio, AUDIO};
