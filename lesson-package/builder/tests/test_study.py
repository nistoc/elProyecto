"""The notes of a lesson cut into study items (made-up notes only)."""
import unittest

from builder.study import clean, kind_of, sections, study_of, word_pair

SUMMARY = """# Конспект урока

## 📘 Грамматика

### Pretérito perfecto
Образуется так: haber + participio.
- ES: He comido una manzana; RU: Я съел яблоко

## 🧾 Лексика урока
- **la manzana** — яблоко
- 🍐 **la pera** — груша [добавлено]

| es | ru |
|---|---|
| el limón | лимон |

## 🗣 Модели речи
- ES: ¿Qué has hecho hoy? — RU: Что ты сегодня сделал?

## ✏️ Исправления и примечания
- yo he comer → yo he comido

## 🧪 Практика (самопроверка)
1. Yo ____ (comer) una pera.
2. Ellos ____ (ir) al mercado.

## 🔑 Ключи
1. he comido
2. han ido

## Coverage
- перенесено всё
"""

DRILL = """## Практика
Поставьте в женском роде.
**Ключи:** 1) ciudad francesa; 2) canción alemana.
1. vino francés → ciudad ...
2. coche alemán → canción ...
"""

HOMEWORK = """## Задание 1. Переведите
1. Я купил хлеб.
2. Мы видели фильм.

## Задание 2
- Ella ____ (ser) muy amable.
- [ ] выучить слова
"""


class Clean(unittest.TestCase):
    def test_marks_emoji_and_added_tags_go(self):
        self.assertEqual(clean('- 🍐 **la pera** [добавлено]'), 'la pera')

    def test_a_wiki_link_keeps_its_text(self):
        self.assertEqual(clean('[[Урок|el urok]] y más'), 'el urok y más')


class Pairs(unittest.TestCase):
    def test_shapes_of_a_word_line(self):
        self.assertEqual(word_pair('- **la casa** — дом'), ('la casa', 'дом'))
        self.assertEqual(word_pair('la mesa;стол'), ('la mesa', 'стол'))
        self.assertEqual(word_pair('ES: Hola, ¿qué tal?; RU: Привет, как дела?'), ('Hola, ¿qué tal?', 'Привет, как дела?'))

    def test_a_table_row_and_a_russian_line_are_not_pairs(self):
        self.assertIsNone(word_pair('| el limón | лимон |'))
        self.assertIsNone(word_pair('- это — то'))


class Kinds(unittest.TestCase):
    def test_kind_by_the_nearest_heading(self):
        self.assertEqual(kind_of(['Конспект', 'Грамматика', 'Pretérito perfecto'], 'summary'), 'grammar')
        self.assertEqual(kind_of(['Конспект', 'Лексика урока'], 'summary'), 'words')
        self.assertEqual(kind_of(['Конспект', 'Ключи'], 'summary'), 'keys')

    def test_practice_above_wins_and_service_sections_are_dropped(self):
        self.assertEqual(kind_of(['Практика', 'A) Грамматика'], 'summary'), 'practice')
        self.assertIsNone(kind_of(['Конспект', 'Coverage'], 'summary'))

    def test_homework_named_in_a_summary_is_a_task(self):
        self.assertEqual(kind_of(['Конспект', 'Tarea'], 'summary'), 'homework')
        self.assertEqual(kind_of(['Конспект', 'Дзюдо'], 'summary'), 'reading')

    def test_a_homework_note_is_tasks(self):
        self.assertEqual(kind_of(['Задание 2'], 'homework'), 'homework')
        self.assertEqual(kind_of(['Лексика'], 'homework'), 'homework')

    def test_sections_follow_the_headings_and_skip_fences(self):
        got = sections('# A\ntext\n```\n# not a heading\n```\n## B\nmore')
        self.assertEqual([s['titles'] for s in got], [['A'], ['A', 'B']])


class StudyOf(unittest.TestCase):
    def setUp(self):
        self.st = study_of([{'id': 's', 'title': 'Конспект', 'kind': 'summary', 'markdown': SUMMARY},
                            {'id': 'h', 'title': 'ДЗ', 'kind': 'homework', 'markdown': HOMEWORK},
                            {'id': 'l', 'title': 'Журнал', 'kind': 'log', 'markdown': '- la mesa — стол'}])

    def test_words_of_lines_and_tables_once_each(self):
        words = [(w['es'], w['ru']) for w in self.st['words']]
        self.assertEqual(words, [('la manzana', 'яблоко'), ('la pera', 'груша'), ('el limón', 'лимон')])

    def test_phrases_of_es_ru_lines(self):
        es = [p['es'] for p in self.st['phrases']]
        self.assertIn('¿Qué has hecho hoy?', es)
        self.assertIn('He comido una manzana', es)

    def test_a_mistake_is_wrong_and_right(self):
        self.assertEqual([(m['wrong'], m['right']) for m in self.st['mistakes']], [('yo he comer', 'yo he comido')])

    def test_a_grammar_card_keeps_its_rule_and_examples(self):
        card = [c for c in self.st['cards'] if c['kind'] == 'grammar'][0]
        self.assertEqual(card['title'], 'Pretérito perfecto')
        self.assertIn('haber + participio', card['md'])
        self.assertEqual(card['examples'], [{'es': 'He comido una manzana', 'ru': 'Я съел яблоко'}])

    def test_practice_items_keep_their_gaps_and_get_the_keys(self):
        task = [t for t in self.st['tasks'] if t['kind'] == 'practice'][0]
        self.assertEqual(task['items'], ['Yo ____ (comer) una pera', 'Ellos ____ (ir) al mercado'])
        self.assertIn('han ido', task['keys'])
        self.assertNotIn('Yo ____', task['md'])

    def test_homework_items_translate_lines_and_bullet_gaps(self):
        tasks = [t for t in self.st['tasks'] if t['note'] == 'h']
        self.assertEqual(tasks[0]['items'], ['Я купил хлеб', 'Мы видели фильм'])
        self.assertEqual(tasks[1]['items'], ['Ella ____ (ser) muy amable'])
        self.assertIn('[ ] выучить слова', tasks[1]['md'])

    def test_keys_in_the_text_of_a_task_are_hidden_and_dots_are_a_gap(self):
        task = study_of([{'id': 'd', 'title': 'Упражнения', 'kind': 'other', 'markdown': DRILL}])['tasks'][0]
        self.assertEqual(task['items'], ['vino francés → ciudad ____', 'coche alemán → canción ____'])
        self.assertIn('ciudad francesa', task['keys'])
        self.assertNotIn('Ключи', task['md'])

    def test_service_sections_and_logs_give_nothing(self):
        self.assertFalse(any('перенесено' in (c['md'] or '') for c in self.st['cards']))
        self.assertNotIn('la mesa', [w['es'] for w in self.st['words']])


if __name__ == '__main__':
    unittest.main()
