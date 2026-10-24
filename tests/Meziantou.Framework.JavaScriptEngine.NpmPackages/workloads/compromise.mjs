// compromise: natural language processing (tokenization, part-of-speech tagging with a large lexicon, sentence
// parsing, verb conjugation, noun inflection, number parsing and pattern matching)
import nlp from "compromise";

const text = "John Smith walked quickly to the old store on Main Street yesterday. He didn't buy anything! Would they have gone there twice, if prices were lower? The company's 3 new products cost $25.50 each.";

export default () => {
  const document = nlp(text);
  return JSON.stringify({
    sentences: document.sentences().json().map(sentence => sentence.text),
    terms: document.terms().json().map(term => [term.text, term.terms[0].tags.join(",")]),
    people: document.people().out("array"),
    places: document.places().out("array"),
    verbs: document.verbs().json().map(verb => [verb.text, verb.verb?.infinitive, verb.verb?.grammar?.tense]),
    conjugate: nlp("she sells seashells").verbs().conjugate(),
    pastTense: nlp("I walk to the store and eat an apple").sentences().toPastTense().text(),
    negate: nlp("the cat is sleeping").sentences().toNegative().text(),
    plural: nlp("the mouse ate a cherry").nouns().toPlural().all().text(),
    numbers: nlp("I have twenty-two dogs and 1,500 cats, the third one is 2.5kg").numbers().json().map(number => [number.text, number.number?.num]),
    contractions: nlp("we've gone and they'd stay").contractions().expand().all().text(),
    matches: [document.match("#Adjective #Noun").out("array"), document.match("[#Person] walked", 0).out("array"), document.match("(store|products)").out("array")],
    normalized: nlp("  The   QUICK   brown fox!!  ").normalize({ whitespace: true, case: true, punctuation: true }).text(),
    // topics() is not used: it sorts with a comparator that returns -1 for every pair, and the order of such a sort is
    // implementation-defined
    organizations: nlp("Google and the World Health Organization met at Microsoft").organizations().out("array"),
  });
};
