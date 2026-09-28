namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class MakefileHighlighterTests
{
    [Fact]
    public void Comments()
    {
        AssertHighlighter("makefile",
"""
# Build configuration
CC = gcc # the compiler
""",
"""
<span class="hljs-comment"># Build configuration</span>
CC = gcc <span class="hljs-comment"># the compiler</span>
""");
    }

    [Fact]
    public void Assignments()
    {
        AssertHighlighter("makefile",
"""
CC = gcc
CFLAGS := -Wall -O2
PREFIX ?= /usr/local
LDFLAGS += -lm
SHELL != which bash
X::=1
""",
"""
CC = gcc
CFLAGS := -Wall -O2
PREFIX ?= /usr/local
LDFLAGS += -lm
SHELL != which bash
X::=1
""");
    }

    [Fact]
    public void AssignmentsWithoutSpaces()
    {
        AssertHighlighter("makefile",
"""
CC=gcc
CXX:=g++
""",
"""
CC=gcc
CXX:=g++
""");
    }

    [Fact]
    public void PosixAssignments()
    {
        AssertHighlighter("makefile",
"""
A ::= 1
B :::= 2
C != date
D::=1
""",
"""
A ::= 1
B :::= 2
C != date
D::=1
""");
    }

    [Fact]
    public void SimpleRule()
    {
        AssertHighlighter("makefile",
"""
all: main.o util.o
	$(CC) -o app main.o util.o
""",
"""
<span class="hljs-section">all: main.o util.o</span>
	<span class="hljs-variable">$(CC)</span> -o app main.o util.o
""");
    }

    [Fact]
    public void Phony()
    {
        AssertHighlighter("makefile",
"""
.PHONY: all clean install
""",
"""
<span class="hljs-meta"><span class="hljs-keyword">.PHONY</span>: all clean install</span>
""");
    }

    [Fact]
    public void PhonyAfterRule()
    {
        AssertHighlighter("makefile",
"""
all: build
.PHONY: all
""",
"""
<span class="hljs-section">all: build</span>
<span class="hljs-meta"><span class="hljs-keyword">.PHONY</span>: all</span>
""");
    }

    [Fact]
    public void PatternRule()
    {
        AssertHighlighter("makefile",
"""
%.o: %.c
	$(CC) $(CFLAGS) -c $< -o $@
""",
"""
<span class="hljs-section">%.o: %.c</span>
	<span class="hljs-variable">$(CC)</span> <span class="hljs-variable">$(CFLAGS)</span> -c <span class="hljs-variable">$&lt;</span> -o <span class="hljs-variable">$@</span>
""");
    }

    [Fact]
    public void AutomaticVariables()
    {
        AssertHighlighter("makefile",
"""
app: $(OBJS)
	$(CC) -o $@ $^ $+ $* $? $%
""",
"""
<span class="hljs-section">app: <span class="hljs-variable">$(OBJS)</span></span>
	<span class="hljs-variable">$(CC)</span> -o <span class="hljs-variable">$@</span> <span class="hljs-variable">$^</span> <span class="hljs-variable">$+</span> <span class="hljs-variable">$*</span> <span class="hljs-variable">$?</span> <span class="hljs-variable">$%</span>
""");
    }

    [Fact]
    public void AutomaticVariableDirectoryAndFile()
    {
        AssertHighlighter("makefile",
"""
out/%.o: %.c
	@mkdir -p $(@D)
	$(CC) -c $< -o $(@F)
""",
"""
<span class="hljs-section">out/%.o: %.c</span>
	@mkdir -p <span class="hljs-variable">$(@D)</span>
	<span class="hljs-variable">$(CC)</span> -c <span class="hljs-variable">$&lt;</span> -o <span class="hljs-variable">$(@F)</span>
""");
    }

    [Fact]
    public void BracedVariables()
    {
        AssertHighlighter("makefile",
"""
SRC = ${SRCDIR}/main.c
	echo ${HOME}
""",
"""
SRC = <span class="hljs-variable">${SRCDIR}</span>/main.c
	echo <span class="hljs-variable">${HOME}</span>
""");
    }

    [Fact]
    public void Functions()
    {
        AssertHighlighter("makefile",
"""
SRCS := $(wildcard src/*.c)
OBJS := $(patsubst %.c,%.o,$(SRCS))
NAMES = $(notdir $(basename $(SRCS)))
""",
"""
SRCS := <span class="hljs-variable">$(<span class="hljs-built_in">wildcard</span> src/*.c)</span>
OBJS := <span class="hljs-variable">$(<span class="hljs-built_in">patsubst</span> %.c,%.o,<span class="hljs-variable">$(SRCS)</span>)</span>
NAMES = <span class="hljs-variable">$(<span class="hljs-built_in">notdir</span> <span class="hljs-variable">$(<span class="hljs-built_in">basename</span> <span class="hljs-variable">$(SRCS)</span>)</span>)</span>
""");
    }

    [Fact]
    public void ShellFunction()
    {
        AssertHighlighter("makefile",
"""
GIT_SHA := $(shell git rev-parse --short HEAD)
DATE = $(shell date +%Y-%m-%d)
""",
"""
GIT_SHA := <span class="hljs-variable">$(<span class="hljs-built_in">shell</span> git rev-parse --short HEAD)</span>
DATE = <span class="hljs-variable">$(<span class="hljs-built_in">shell</span> date +%Y-%m-%d)</span>
""");
    }

    [Fact]
    public void SubstitutionReferences()
    {
        AssertHighlighter("makefile",
"""
OBJS = $(SRCS:.c=.o)
DEPS = $(OBJS:%.o=%.d)
""",
"""
OBJS = <span class="hljs-variable">$(SRCS:.c=.o)</span>
DEPS = <span class="hljs-variable">$(OBJS:%.o=%.d)</span>
""");
    }

    [Fact]
    public void BracedSubstitutionReferences()
    {
        AssertHighlighter("makefile",
"""
OBJS = ${SRCS:.c=.o}
X = ${A:%=${B}/%}
""",
"""
OBJS = <span class="hljs-variable">${SRCS:.c=.o}</span>
X = <span class="hljs-variable">${A:%=<span class="hljs-variable">${B}</span>/%}</span>
""");
    }

    [Fact]
    public void Conditionals()
    {
        AssertHighlighter("makefile",
"""
ifeq ($(OS),Windows_NT)
  RM = del /Q
else
  RM = rm -f
endif
ifdef DEBUG
CFLAGS += -g
endif
ifneq "$(CC)" "gcc"
endif
ifndef VERBOSE
.SILENT:
endif
""",
"""
<span class="hljs-keyword">ifeq</span> (<span class="hljs-variable">$(OS)</span>,Windows_NT)
  RM = del /Q
<span class="hljs-keyword">else</span>
  RM = rm -f
<span class="hljs-keyword">endif</span>
<span class="hljs-keyword">ifdef</span> DEBUG
CFLAGS += -g
<span class="hljs-keyword">endif</span>
<span class="hljs-keyword">ifneq</span> <span class="hljs-string">&quot;<span class="hljs-variable">$(CC)</span>&quot;</span> <span class="hljs-string">&quot;gcc&quot;</span>
<span class="hljs-keyword">endif</span>
<span class="hljs-keyword">ifndef</span> VERBOSE
<span class="hljs-section">.SILENT:</span>
<span class="hljs-keyword">endif</span>
""");
    }

    [Fact]
    public void Define()
    {
        AssertHighlighter("makefile",
"""
define HELP_TEXT
Usage: make [target]
  all    Build everything
endef
export HELP_TEXT
""",
"""
<span class="hljs-keyword">define</span> HELP_TEXT
<span class="hljs-section">Usage: make [target]</span>
  all    Build everything
<span class="hljs-keyword">endef</span>
<span class="hljs-keyword">export</span> HELP_TEXT
""");
    }

    [Fact]
    public void Include()
    {
        AssertHighlighter("makefile",
"""
include config.mk
-include $(DEPS)
sinclude local.mk
""",
"""
<span class="hljs-keyword">include</span> config.mk
<span class="hljs-keyword">-include</span> <span class="hljs-variable">$(DEPS)</span>
<span class="hljs-keyword">sinclude</span> local.mk
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("makefile",
"""
MSG = "Hello $(USER) \"quoted\""
	@echo "Building $@"
	@echo 'single $(X)'
""",
"""
MSG = <span class="hljs-string">&quot;Hello <span class="hljs-variable">$(USER)</span> \&quot;quoted\&quot;&quot;</span>
	@echo <span class="hljs-string">&quot;Building <span class="hljs-variable">$@</span>&quot;</span>
	@echo &#x27;single <span class="hljs-variable">$(X)</span>&#x27;
""");
    }

    [Fact]
    public void FunctionInString()
    {
        AssertHighlighter("makefile",
"""
all:
	@echo "Version: $(shell cat VERSION)"
""",
"""
<span class="hljs-section">all:</span>
	@echo <span class="hljs-string">&quot;Version: <span class="hljs-variable">$(<span class="hljs-built_in">shell</span> cat VERSION)</span>&quot;</span>
""");
    }

    [Fact]
    public void Recipe()
    {
        AssertHighlighter("makefile",
"""
clean:
	rm -rf build/ *.o
	@echo Done
""",
"""
<span class="hljs-section">clean:</span>
	rm -rf build/ *.o
	@echo Done
""");
    }

    [Fact]
    public void TargetIsVariable()
    {
        AssertHighlighter("makefile",
"""
$(TARGET): $(OBJS)
	$(LD) $(LDFLAGS) $^ -o $@
""",
"""
<span class="hljs-variable">$(TARGET)</span>: <span class="hljs-variable">$(OBJS)</span>
	<span class="hljs-variable">$(LD)</span> <span class="hljs-variable">$(LDFLAGS)</span> <span class="hljs-variable">$^</span> -o <span class="hljs-variable">$@</span>
""");
    }

    [Fact]
    public void FunctionInTarget()
    {
        AssertHighlighter("makefile",
"""
all: $(wildcard *.c) $(X:.c=.o)
""",
"""
<span class="hljs-section">all: <span class="hljs-variable">$(<span class="hljs-built_in">wildcard</span> *.c)</span> <span class="hljs-variable">$(X:.c=.o)</span></span>
""");
    }

    [Fact]
    public void DoubleColonRule()
    {
        AssertHighlighter("makefile",
"""
install:: all
	cp app $(PREFIX)/bin
""",
"""
<span class="hljs-section">install:: all</span>
	cp app <span class="hljs-variable">$(PREFIX)</span>/bin
""");
    }

    [Fact]
    public void MultipleTargets()
    {
        AssertHighlighter("makefile",
"""
foo bar: baz
	touch $@
""",
"""
foo bar: baz
	touch <span class="hljs-variable">$@</span>
""");
    }

    [Fact]
    public void OrderOnlyPrerequisites()
    {
        AssertHighlighter("makefile",
"""
build/app: main.c | build
	$(CC) $< -o $@
build:
	mkdir -p $@
""",
"""
<span class="hljs-section">build/app: main.c | build</span>
	<span class="hljs-variable">$(CC)</span> <span class="hljs-variable">$&lt;</span> -o <span class="hljs-variable">$@</span>
<span class="hljs-section">build:</span>
	mkdir -p <span class="hljs-variable">$@</span>
""");
    }

    [Fact]
    public void EscapedDollar()
    {
        AssertHighlighter("makefile",
"""
list:
	for f in *.c; do echo $$f; done
	@echo $$HOME
""",
"""
<span class="hljs-section">list:</span>
	for f in *.c; do echo $$f; done
	@echo $$HOME
""");
    }

    [Fact]
    public void EscapedDollarBeforeVariableCharacters()
    {
        AssertHighlighter("makefile",
"""
all:
	echo $$@ $$(pwd) $$$$ $$$(X)
""",
"""
<span class="hljs-section">all:</span>
	echo $$@ $$(pwd) $$$$ $$<span class="hljs-variable">$(X)</span>
""");
    }

    [Fact]
    public void LineContinuation()
    {
        AssertHighlighter("makefile",
"""
SRCS = main.c \
       util.c \
       io.c
""",
"""
SRCS = main.c \
       util.c \
       io.c
""");
    }

    [Fact]
    public void Directives()
    {
        AssertHighlighter("makefile",
"""
export CC
unexport DEBUG
override CFLAGS += -Werror
private X = 1
vpath %.c src
undefine FOO
""",
"""
<span class="hljs-keyword">export</span> CC
<span class="hljs-keyword">unexport</span> DEBUG
<span class="hljs-keyword">override</span> CFLAGS += -Werror
<span class="hljs-keyword">private</span> X = 1
<span class="hljs-keyword">vpath</span> %.c src
<span class="hljs-keyword">undefine</span> FOO
""");
    }

    [Fact]
    public void ForeachAndCall()
    {
        AssertHighlighter("makefile",
"""
DIRS = a b c
all:
	$(foreach d,$(DIRS),$(MAKE) -C $(d);)
reverse = $(2) $(1)
foo = $(call reverse,a,b)
$(eval $(call template,x))
""",
"""
DIRS = a b c
<span class="hljs-section">all:</span>
	<span class="hljs-variable">$(<span class="hljs-built_in">foreach</span> d,<span class="hljs-variable">$(DIRS)</span>,<span class="hljs-variable">$(MAKE)</span> -C <span class="hljs-variable">$(d)</span>;)</span>
reverse = $(2) $(1)
foo = <span class="hljs-variable">$(<span class="hljs-built_in">call</span> reverse,a,b)</span>
<span class="hljs-variable">$(<span class="hljs-built_in">eval</span> <span class="hljs-variable">$(<span class="hljs-built_in">call</span> template,x)</span>)</span>
""");
    }

    [Fact]
    public void ErrorWarningInfo()
    {
        AssertHighlighter("makefile",
"""
ifeq ($(strip $(CC)),)
$(error CC is not set)
endif
$(warning deprecated)
$(info building)
""",
"""
<span class="hljs-keyword">ifeq</span> (<span class="hljs-variable">$(<span class="hljs-built_in">strip</span> <span class="hljs-variable">$(CC)</span>)</span>,)
<span class="hljs-variable">$(<span class="hljs-built_in">error</span> CC is not set)</span>
<span class="hljs-keyword">endif</span>
<span class="hljs-variable">$(<span class="hljs-built_in">warning</span> deprecated)</span>
<span class="hljs-variable">$(<span class="hljs-built_in">info</span> building)</span>
""");
    }

    [Fact]
    public void InfoWordsLet()
    {
        AssertHighlighter("makefile",
"""
$(info $(words $(SRCS)) files)
$(let a b,1 2,$(a))
""",
"""
<span class="hljs-variable">$(<span class="hljs-built_in">info</span> <span class="hljs-variable">$(<span class="hljs-built_in">words</span> <span class="hljs-variable">$(SRCS)</span>)</span> files)</span>
<span class="hljs-variable">$(<span class="hljs-built_in">let</span> a b,1 2,<span class="hljs-variable">$(a)</span>)</span>
""");
    }

    [Fact]
    public void TargetSpecificVariable()
    {
        AssertHighlighter("makefile",
"""
debug: CFLAGS += -g
debug: all
""",
"""
<span class="hljs-section">debug: CFLAGS += -g</span>
<span class="hljs-section">debug: all</span>
""");
    }

    [Fact]
    public void StaticPatternRule()
    {
        AssertHighlighter("makefile",
"""
$(OBJS): %.o: %.c
	$(CC) -c $< -o $@
""",
"""
<span class="hljs-variable">$(OBJS)</span>: %.o: %.c
	<span class="hljs-variable">$(CC)</span> -c <span class="hljs-variable">$&lt;</span> -o <span class="hljs-variable">$@</span>
""");
    }

    [Fact]
    public void SpecialTargets()
    {
        AssertHighlighter("makefile",
"""
.DEFAULT_GOAL := all
.SUFFIXES:
.ONESHELL:
.DELETE_ON_ERROR:
""",
"""
.DEFAULT_GOAL := all
<span class="hljs-section">.SUFFIXES:</span>
<span class="hljs-section">.ONESHELL:</span>
<span class="hljs-section">.DELETE_ON_ERROR:</span>
""");
    }

    [Fact]
    public void UrlInRecipe()
    {
        AssertHighlighter("makefile",
"""
download:
	curl -o file https://example.com/x.tar.gz
""",
"""
<span class="hljs-section">download:</span>
	curl -o file https://example.com/x.tar.gz
""");
    }

    [Fact]
    public void CommentInRecipe()
    {
        AssertHighlighter("makefile",
"""
all:
	# not a make comment
	echo hi # trailing
""",
"""
<span class="hljs-section">all:</span>
	<span class="hljs-comment"># not a make comment</span>
	echo hi <span class="hljs-comment"># trailing</span>
""");
    }

    [Fact]
    public void EscapedHash()
    {
        AssertHighlighter("makefile",
"""
X = a\#b
""",
"""
X = a\#b
""");
    }

    [Fact]
    public void KeywordsInsideWords()
    {
        AssertHighlighter("makefile",
"""
CFLAGS = -Iinclude -Iexport
""",
"""
CFLAGS = -Iinclude -Iexport
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("makefile",
"""
VERSION = 1.2.3
JOBS = 4
""",
"""
VERSION = 1.2.3
JOBS = 4
""");
    }

    [Fact]
    public void RealisticMakefile()
    {
        AssertHighlighter("makefile",
"""
# Simple C project
CC      ?= cc
CFLAGS  ?= -std=c11 -Wall -Wextra -O2
SRCDIR  := src
BUILD   := build
SRCS    := $(wildcard $(SRCDIR)/*.c)
OBJS    := $(SRCS:$(SRCDIR)/%.c=$(BUILD)/%.o)
TARGET  := $(BUILD)/app

.PHONY: all clean run

all: $(TARGET)

$(TARGET): $(OBJS)
	$(CC) $(CFLAGS) -o $@ $^

$(BUILD)/%.o: $(SRCDIR)/%.c | $(BUILD)
	$(CC) $(CFLAGS) -c $< -o $@

$(BUILD):
	mkdir -p $@

run: all
	./$(TARGET)

clean:
	$(RM) -r $(BUILD)
""",
"""
<span class="hljs-comment"># Simple C project</span>
CC      ?= cc
CFLAGS  ?= -std=c11 -Wall -Wextra -O2
SRCDIR  := src
BUILD   := build
SRCS    := <span class="hljs-variable">$(<span class="hljs-built_in">wildcard</span> <span class="hljs-variable">$(SRCDIR)</span>/*.c)</span>
OBJS    := <span class="hljs-variable">$(SRCS:<span class="hljs-variable">$(SRCDIR)</span>/%.c=<span class="hljs-variable">$(BUILD)</span>/%.o)</span>
TARGET  := <span class="hljs-variable">$(BUILD)</span>/app

<span class="hljs-meta"><span class="hljs-keyword">.PHONY</span>: all clean run</span>

<span class="hljs-section">all: <span class="hljs-variable">$(TARGET)</span></span>

<span class="hljs-variable">$(TARGET)</span>: <span class="hljs-variable">$(OBJS)</span>
	<span class="hljs-variable">$(CC)</span> <span class="hljs-variable">$(CFLAGS)</span> -o <span class="hljs-variable">$@</span> <span class="hljs-variable">$^</span>

<span class="hljs-variable">$(BUILD)</span>/%.o: <span class="hljs-variable">$(SRCDIR)</span>/%.c | <span class="hljs-variable">$(BUILD)</span>
	<span class="hljs-variable">$(CC)</span> <span class="hljs-variable">$(CFLAGS)</span> -c <span class="hljs-variable">$&lt;</span> -o <span class="hljs-variable">$@</span>

<span class="hljs-variable">$(BUILD)</span>:
	mkdir -p <span class="hljs-variable">$@</span>

<span class="hljs-section">run: all</span>
	./<span class="hljs-variable">$(TARGET)</span>

<span class="hljs-section">clean:</span>
	<span class="hljs-variable">$(RM)</span> -r <span class="hljs-variable">$(BUILD)</span>
""");
    }

    [Fact]
    public void WindowsPathTarget()
    {
        AssertHighlighter("makefile",
"""
C:/out/app.exe: main.c
""",
"""
<span class="hljs-section">C:/out/app.exe: main.c</span>
""");
    }

    [Fact]
    public void VariableNameWithDash()
    {
        AssertHighlighter("makefile",
"""
my-var = 1
$(my-var)
""",
"""
my-var = 1
<span class="hljs-variable">$(my-var)</span>
""");
    }

    [Fact]
    public void FunctionWithStrings()
    {
        AssertHighlighter("makefile",
"""
X = $(if $(DEBUG),"debug","release")
""",
"""
X = <span class="hljs-variable">$(<span class="hljs-built_in">if</span> <span class="hljs-variable">$(DEBUG)</span>,<span class="hljs-string">&quot;debug&quot;</span>,<span class="hljs-string">&quot;release&quot;</span>)</span>
""");
    }
}
