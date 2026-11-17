#!/usr/bin/env python3
"""Puts the editor page together from its parts in tools/editor/src/.

The page stays one file that opens offline from disk; the parts only make it easier to work on. They are pieces of one
page and one script, joined in file-name order exactly as they are: no part is valid on its own, and nothing is added
or changed between them.

  python3 tools/editor/build.py           writes tools/editor/index.html
  python3 tools/editor/build.py --check   exit 1 when index.html is not what the parts make (edited by hand, or not rebuilt)
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, 'src')
PAGE = os.path.join(HERE, 'index.html')


def parts():
    names = sorted(n for n in os.listdir(SRC) if n.endswith(('.html', '.js')))
    if not names:
        sys.exit('no parts in ' + SRC)
    return names


def page():
    return b''.join(open(os.path.join(SRC, n), 'rb').read() for n in parts())


def main():
    made = page()
    for need in (b'src="three.min.js"', b"'__ML_PAGE_STAMP__'"):
        if need not in made:
            sys.exit('the page lost ' + need.decode() + ', which the package and the game rely on')
    if '--check' in sys.argv[1:]:
        have = open(PAGE, 'rb').read() if os.path.exists(PAGE) else b''
        if have != made:
            print('tools/editor/index.html is not what tools/editor/src makes: run python3 tools/editor/build.py')
            return 1
        print('tools/editor/index.html matches its ' + str(len(parts())) + ' parts')
        return 0
    open(PAGE, 'wb').write(made)
    print('tools/editor/index.html written from ' + str(len(parts())) + ' parts, ' + str(len(made)) + ' bytes')
    return 0


if __name__ == '__main__':
    sys.exit(main())
