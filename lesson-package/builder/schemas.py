"""JSON schemas of the answers (strict: every field required, no other fields)."""

S = {'type': 'string'}
I = {'type': 'integer'}
B = {'type': 'boolean'}


def obj(props):
    return {'type': 'object', 'properties': props, 'required': list(props), 'additionalProperties': False}


def arr(props):
    return {'type': 'array', 'items': obj(props)}


HOW = {'type': 'string', 'enum': ['explained', 'asked', 'corrected', 'prompted', 'introduced']}
HOW_C = {'type': 'string', 'enum': ['explained', 'asked', 'corrected', 'prompted', 'introduced', 'none']}
KIND = {'type': 'string', 'enum': ['explained', 'corrected', 'asked']}
POS = {'type': 'string', 'enum': ['noun', 'verb', 'adjective', 'adverb', 'expression']}
EXAMPLES = arr({'line': I, 'quote': S})

# first form
_GRAMMAR_1 = arr({'topic': S, 'rule': S, 'kind': KIND, 'examples': EXAMPLES})
_WORDS_1 = arr({'es': S, 'ru': S, 'how': HOW, 'line': I, 'quote': S, 'note': S})
FIRST_A = obj({'topics': arr({'title': S, 'summary': S, 'from_line': I, 'to_line': I}), 'grammar': _GRAMMAR_1,
               'examples': arr({'line': I, 'quote': S, 'ru': S, 'shows': S})})
FIRST_B = obj({'new_words': _WORDS_1, 'vocabulary': arr({'es': S, 'ru': S, 'pos': POS, 'line': I, 'quote': S})})
FIRST_C = obj({'new_words': _WORDS_1, 'grammar': _GRAMMAR_1})

# passes 1, 2
GRAMMAR_THEMED = arr({'theme': S, 'topic': S, 'rule': S, 'kind': KIND, 'examples': EXAMPLES})
PASS1 = obj({'topics': arr({'title': S, 'summary': S, 'from_line': I, 'to_line': I}), 'grammar': GRAMMAR_THEMED,
             'examples': arr({'line': I, 'quote': S, 'ru': S, 'shows': S})})
PASS2 = obj({'new_words': arr({'es': S, 'heard': S, 'ru': S, 'how': HOW, 'line': I, 'quote': S, 'note': S}),
             'vocabulary': arr({'es': S, 'ru': S, 'pos': POS, 'line': I, 'quote': S})})
# pass 3
PASS3 = obj({'words': arr({'i': I, 'taught': B, 'es': S, 'ru': S, 'how': HOW_C, 'evidence': S})})
# pass 4
PASS4 = obj({'grammar': GRAMMAR_THEMED})
# pass 5
PASS5 = obj({'groups': arr({'theme': S, 'title': S, 'rule': S, 'ids': {'type': 'array', 'items': I}, 'is_grammar': B})})
# pass 6
PASS6 = obj({'items': arr({'i': I, 'spanish': B, 'form': {'type': 'string', 'enum': ['dictionary', 'plural', 'verb_form']},
                           'lemma': S, 'about': {'type': 'string', 'enum': ['word', 'grammar', 'passing']}})})
# pass 7
PASS7 = obj({'items': arr({'i': I, 'kind': {'type': 'string', 'enum': ['spanish', 'russian', 'other', 'name', 'broken']},
                           'lemma': S, 'ru': S, 'sure': B})})
