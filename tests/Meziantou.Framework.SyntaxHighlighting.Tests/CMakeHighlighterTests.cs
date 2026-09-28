namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class CMakeHighlighterTests
{
    [Fact]
    public void Project()
    {
        AssertHighlighter("cmake",
"""
cmake_minimum_required(VERSION 3.20)
project(MyApp VERSION 1.2.3 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
""",
"""
<span class="hljs-keyword">cmake_minimum_required</span>(VERSION <span class="hljs-number">3.20</span>)
<span class="hljs-keyword">project</span>(MyApp VERSION <span class="hljs-number">1.2</span>.<span class="hljs-number">3</span> LANGUAGES CXX)

<span class="hljs-keyword">set</span>(CMAKE_CXX_STANDARD <span class="hljs-number">20</span>)
<span class="hljs-keyword">set</span>(CMAKE_CXX_STANDARD_REQUIRED <span class="hljs-keyword">ON</span>)
""");
    }

    [Fact]
    public void TargetCommands()
    {
        AssertHighlighter("cmake",
"""
add_executable(app src/main.cpp src/util.cpp)
add_library(core STATIC src/core.cpp)
target_include_directories(core PUBLIC ${CMAKE_CURRENT_SOURCE_DIR}/include)
target_link_libraries(app PRIVATE core fmt::fmt)
target_compile_options(app PRIVATE -Wall -Wextra)
target_compile_definitions(app PRIVATE VERSION="${PROJECT_VERSION}")
""",
"""
<span class="hljs-keyword">add_executable</span>(app src/main.cpp src/util.cpp)
<span class="hljs-keyword">add_library</span>(core STATIC src/core.cpp)
<span class="hljs-keyword">target_include_directories</span>(core PUBLIC <span class="hljs-variable">${CMAKE_CURRENT_SOURCE_DIR}</span>/<span class="hljs-keyword">include</span>)
<span class="hljs-keyword">target_link_libraries</span>(app PRIVATE core fmt::fmt)
<span class="hljs-keyword">target_compile_options</span>(app PRIVATE -Wall -Wextra)
<span class="hljs-keyword">target_compile_definitions</span>(app PRIVATE VERSION=<span class="hljs-string">&quot;<span class="hljs-variable">${PROJECT_VERSION}</span>&quot;</span>)
""");
    }

    [Fact]
    public void Conditions()
    {
        AssertHighlighter("cmake",
"""
if(WIN32)
  add_definitions(-DPLATFORM_WINDOWS)
elseif(APPLE AND NOT IOS)
  message(STATUS "macOS")
else()
  message(WARNING "Unknown platform: ${CMAKE_SYSTEM_NAME}")
endif()

if(DEFINED ENV{CI} OR CMAKE_BUILD_TYPE STREQUAL "Release")
endif()
if(NOT EXISTS "${CMAKE_BINARY_DIR}/conan.cmake")
endif()
if(CMAKE_VERSION VERSION_GREATER_EQUAL 3.24)
endif()
if("${x}" MATCHES "^[a-z]+$")
endif()
""",
"""
<span class="hljs-keyword">if</span>(WIN32)
  <span class="hljs-keyword">add_definitions</span>(-DPLATFORM_WINDOWS)
<span class="hljs-keyword">elseif</span>(APPLE <span class="hljs-keyword">AND</span> <span class="hljs-keyword">NOT</span> IOS)
  <span class="hljs-keyword">message</span>(STATUS <span class="hljs-string">&quot;macOS&quot;</span>)
<span class="hljs-keyword">else</span>()
  <span class="hljs-keyword">message</span>(WARNING <span class="hljs-string">&quot;Unknown platform: <span class="hljs-variable">${CMAKE_SYSTEM_NAME}</span>&quot;</span>)
<span class="hljs-keyword">endif</span>()

<span class="hljs-keyword">if</span>(<span class="hljs-keyword">DEFINED</span> ENV{CI} <span class="hljs-keyword">OR</span> CMAKE_BUILD_TYPE <span class="hljs-keyword">STREQUAL</span> <span class="hljs-string">&quot;Release&quot;</span>)
<span class="hljs-keyword">endif</span>()
<span class="hljs-keyword">if</span>(<span class="hljs-keyword">NOT</span> <span class="hljs-keyword">EXISTS</span> <span class="hljs-string">&quot;<span class="hljs-variable">${CMAKE_BINARY_DIR}</span>/conan.cmake&quot;</span>)
<span class="hljs-keyword">endif</span>()
<span class="hljs-keyword">if</span>(CMAKE_VERSION <span class="hljs-keyword">VERSION_GREATER_EQUAL</span> <span class="hljs-number">3.24</span>)
<span class="hljs-keyword">endif</span>()
<span class="hljs-keyword">if</span>(<span class="hljs-string">&quot;<span class="hljs-variable">${x}</span>&quot;</span> <span class="hljs-keyword">MATCHES</span> <span class="hljs-string">&quot;^[a-z]+$&quot;</span>)
<span class="hljs-keyword">endif</span>()
""");
    }

    [Fact]
    public void CommandsAreCaseInsensitive()
    {
        AssertHighlighter("cmake",
"""
IF(UNIX)
  SET(FOO "bar")
  MESSAGE(STATUS "Foo is ${FOO}")
ENDIF(UNIX)
Set(x 1)
""",
"""
<span class="hljs-keyword">IF</span>(UNIX)
  <span class="hljs-keyword">SET</span>(FOO <span class="hljs-string">&quot;bar&quot;</span>)
  <span class="hljs-keyword">MESSAGE</span>(STATUS <span class="hljs-string">&quot;Foo is <span class="hljs-variable">${FOO}</span>&quot;</span>)
<span class="hljs-keyword">ENDIF</span>(UNIX)
<span class="hljs-keyword">Set</span>(x <span class="hljs-number">1</span>)
""");
    }

    [Fact]
    public void Loops()
    {
        AssertHighlighter("cmake",
"""
foreach(src IN LISTS SOURCES)
  get_filename_component(name ${src} NAME_WE)
  list(APPEND OBJECTS ${name}.o)
endforeach()
foreach(i RANGE 0 10 2)
  math(EXPR j "${i} * 2")
endforeach()
while(i LESS 5)
  math(EXPR i "${i} + 1")
endwhile()
""",
"""
<span class="hljs-keyword">foreach</span>(src IN LISTS SOURCES)
  <span class="hljs-keyword">get_filename_component</span>(name <span class="hljs-variable">${src}</span> NAME_WE)
  <span class="hljs-keyword">list</span>(APPEND OBJECTS <span class="hljs-variable">${name}</span>.o)
<span class="hljs-keyword">endforeach</span>()
<span class="hljs-keyword">foreach</span>(i RANGE <span class="hljs-number">0</span> <span class="hljs-number">10</span> <span class="hljs-number">2</span>)
  <span class="hljs-keyword">math</span>(EXPR j <span class="hljs-string">&quot;<span class="hljs-variable">${i}</span> * 2&quot;</span>)
<span class="hljs-keyword">endforeach</span>()
<span class="hljs-keyword">while</span>(i <span class="hljs-keyword">LESS</span> <span class="hljs-number">5</span>)
  <span class="hljs-keyword">math</span>(EXPR i <span class="hljs-string">&quot;<span class="hljs-variable">${i}</span> + 1&quot;</span>)
<span class="hljs-keyword">endwhile</span>()
""");
    }

    [Fact]
    public void FunctionsAndMacros()
    {
        AssertHighlighter("cmake",
"""
function(my_add_test name)
  cmake_parse_arguments(ARG "VERBOSE" "TIMEOUT" "SOURCES;LIBS" ${ARGN})
  add_executable(${name} ${ARG_SOURCES})
  add_test(NAME ${name} COMMAND ${name})
  set_tests_properties(${name} PROPERTIES TIMEOUT 30)
endfunction()

macro(my_macro arg1)
  message("${arg1}")
endmacro()
my_add_test(foo SOURCES foo.cpp)
""",
"""
<span class="hljs-keyword">function</span>(my_add_test name)
  <span class="hljs-keyword">cmake_parse_arguments</span>(ARG <span class="hljs-string">&quot;VERBOSE&quot;</span> <span class="hljs-string">&quot;TIMEOUT&quot;</span> <span class="hljs-string">&quot;SOURCES;LIBS&quot;</span> <span class="hljs-variable">${ARGN}</span>)
  <span class="hljs-keyword">add_executable</span>(<span class="hljs-variable">${name}</span> <span class="hljs-variable">${ARG_SOURCES}</span>)
  <span class="hljs-keyword">add_test</span>(NAME <span class="hljs-variable">${name}</span> <span class="hljs-keyword">COMMAND</span> <span class="hljs-variable">${name}</span>)
  <span class="hljs-keyword">set_tests_properties</span>(<span class="hljs-variable">${name}</span> PROPERTIES TIMEOUT <span class="hljs-number">30</span>)
<span class="hljs-keyword">endfunction</span>()

<span class="hljs-keyword">macro</span>(my_macro arg1)
  <span class="hljs-keyword">message</span>(<span class="hljs-string">&quot;<span class="hljs-variable">${arg1}</span>&quot;</span>)
<span class="hljs-keyword">endmacro</span>()
my_add_test(foo SOURCES foo.cpp)
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("cmake",
"""
# A line comment
set(A 1) # trailing comment
#[[ A bracket
comment ]]
set(B 2)
#[=[ level one ]] still
comment ]=]
set(C 3)
# TODO: fix this
#[[ NOTE: bracket doctag ]]
""",
"""
<span class="hljs-comment"># A line comment</span>
<span class="hljs-keyword">set</span>(A <span class="hljs-number">1</span>) <span class="hljs-comment"># trailing comment</span>
<span class="hljs-comment">#[[ A bracket
comment ]]</span>
<span class="hljs-keyword">set</span>(B <span class="hljs-number">2</span>)
<span class="hljs-comment">#[=[ level one ]] still
comment ]=]</span>
<span class="hljs-keyword">set</span>(C <span class="hljs-number">3</span>)
<span class="hljs-comment"># <span class="hljs-doctag">TODO:</span> fix this</span>
<span class="hljs-comment">#[[ <span class="hljs-doctag">NOTE:</span> bracket doctag ]]</span>
""");
    }

    [Fact]
    public void QuotedArguments()
    {
        AssertHighlighter("cmake",
"""
set(MSG "Hello, World!")
set(ESC "Tab:\t Quote:\" Dollar:\${x}")
set(MULTI "line one
line two ${VAR}")
message("nested ${${prefix}_DIR}")
set(EMPTY "")
""",
"""
<span class="hljs-keyword">set</span>(MSG <span class="hljs-string">&quot;Hello, World!&quot;</span>)
<span class="hljs-keyword">set</span>(ESC <span class="hljs-string">&quot;Tab:\t Quote:\&quot; Dollar:\${x}&quot;</span>)
<span class="hljs-keyword">set</span>(MULTI <span class="hljs-string">&quot;line one
line two <span class="hljs-variable">${VAR}</span>&quot;</span>)
<span class="hljs-keyword">message</span>(<span class="hljs-string">&quot;nested <span class="hljs-variable">${<span class="hljs-variable">${prefix}</span>_DIR}</span>&quot;</span>)
<span class="hljs-keyword">set</span>(EMPTY <span class="hljs-string">&quot;&quot;</span>)
""");
    }

    [Fact]
    public void VariableReferences()
    {
        AssertHighlighter("cmake",
"""
set(path ${CMAKE_SOURCE_DIR}/cmake)
list(APPEND CMAKE_MODULE_PATH ${path})
message(STATUS "Home: $ENV{HOME}")
set(ENV{PATH} "$ENV{PATH}:/opt/bin")
message($CACHE{MY_VAR})
set(nested ${${name}_FOUND})
set(gen $<TARGET_FILE:app>)
set(gen2 "$<$<CONFIG:Debug>:-g>")
""",
"""
<span class="hljs-keyword">set</span>(path <span class="hljs-variable">${CMAKE_SOURCE_DIR}</span>/cmake)
<span class="hljs-keyword">list</span>(APPEND CMAKE_MODULE_PATH <span class="hljs-variable">${path}</span>)
<span class="hljs-keyword">message</span>(STATUS <span class="hljs-string">&quot;Home: <span class="hljs-variable">$ENV{HOME}</span>&quot;</span>)
<span class="hljs-keyword">set</span>(ENV{PATH} <span class="hljs-string">&quot;<span class="hljs-variable">$ENV{PATH}</span>:/opt/bin&quot;</span>)
<span class="hljs-keyword">message</span>(<span class="hljs-variable">$CACHE{MY_VAR}</span>)
<span class="hljs-keyword">set</span>(nested <span class="hljs-variable">${<span class="hljs-variable">${name}</span>_FOUND}</span>)
<span class="hljs-keyword">set</span>(gen $&lt;TARGET_FILE:app&gt;)
<span class="hljs-keyword">set</span>(gen2 <span class="hljs-string">&quot;$&lt;$&lt;CONFIG:Debug&gt;:-g&gt;&quot;</span>)
""");
    }

    [Fact]
    public void BracketArguments()
    {
        AssertHighlighter("cmake",
"""
set(script [[
echo "hello ${not_a_var}"
]])
message([=[ contains ]] inside ]=])
file(WRITE out.txt [==[x]==])
""",
"""
<span class="hljs-keyword">set</span>(script <span class="hljs-string">[[
echo &quot;hello ${not_a_var}&quot;
]]</span>)
<span class="hljs-keyword">message</span>(<span class="hljs-string">[=[ contains ]] inside ]=]</span>)
<span class="hljs-keyword">file</span>(WRITE out.txt <span class="hljs-string">[==[x]==]</span>)
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("cmake",
"""
set(N 42)
set(F 3.14)
set(V 1.2.3)
math(EXPR r "10 / 3")
set(HEX 0x1F)
set(NEG -5)
""",
"""
<span class="hljs-keyword">set</span>(N <span class="hljs-number">42</span>)
<span class="hljs-keyword">set</span>(F <span class="hljs-number">3.14</span>)
<span class="hljs-keyword">set</span>(V <span class="hljs-number">1.2</span>.<span class="hljs-number">3</span>)
<span class="hljs-keyword">math</span>(EXPR r <span class="hljs-string">&quot;10 / 3&quot;</span>)
<span class="hljs-keyword">set</span>(HEX <span class="hljs-number">0</span>x1F)
<span class="hljs-keyword">set</span>(NEG -<span class="hljs-number">5</span>)
""");
    }

    [Fact]
    public void FindPackage()
    {
        AssertHighlighter("cmake",
"""
find_package(Boost 1.70 REQUIRED COMPONENTS system filesystem)
find_package(Threads REQUIRED)
if(Boost_FOUND)
  include_directories(${Boost_INCLUDE_DIRS})
endif()
option(BUILD_TESTS "Build the tests" ON)
option(USE_FOO "Use foo" OFF)
""",
"""
<span class="hljs-keyword">find_package</span>(Boost <span class="hljs-number">1.70</span> REQUIRED COMPONENTS system filesystem)
<span class="hljs-keyword">find_package</span>(Threads REQUIRED)
<span class="hljs-keyword">if</span>(Boost_FOUND)
  <span class="hljs-keyword">include_directories</span>(<span class="hljs-variable">${Boost_INCLUDE_DIRS}</span>)
<span class="hljs-keyword">endif</span>()
<span class="hljs-keyword">option</span>(BUILD_TESTS <span class="hljs-string">&quot;Build the tests&quot;</span> <span class="hljs-keyword">ON</span>)
<span class="hljs-keyword">option</span>(USE_FOO <span class="hljs-string">&quot;Use foo&quot;</span> <span class="hljs-keyword">OFF</span>)
""");
    }

    [Fact]
    public void Install()
    {
        AssertHighlighter("cmake",
"""
install(TARGETS app core
  RUNTIME DESTINATION bin
  LIBRARY DESTINATION lib
  ARCHIVE DESTINATION lib)
install(FILES include/core.h DESTINATION include)
configure_file(config.h.in ${CMAKE_BINARY_DIR}/config.h @ONLY)
""",
"""
<span class="hljs-keyword">install</span>(TARGETS app core
  RUNTIME DESTINATION bin
  LIBRARY DESTINATION lib
  ARCHIVE DESTINATION lib)
<span class="hljs-keyword">install</span>(FILES <span class="hljs-keyword">include</span>/core.h DESTINATION <span class="hljs-keyword">include</span>)
<span class="hljs-keyword">configure_file</span>(config.h.in <span class="hljs-variable">${CMAKE_BINARY_DIR}</span>/config.h @ONLY)
""");
    }

    [Fact]
    public void CustomCommand()
    {
        AssertHighlighter("cmake",
"""
add_custom_command(
  OUTPUT ${CMAKE_BINARY_DIR}/generated.cpp
  COMMAND python3 ${CMAKE_SOURCE_DIR}/gen.py > generated.cpp
  DEPENDS gen.py
  COMMENT "Generating code"
  VERBATIM)
add_custom_target(docs ALL DEPENDS generated.cpp)
execute_process(COMMAND hg id OUTPUT_VARIABLE SHA OUTPUT_STRIP_TRAILING_WHITESPACE)
""",
"""
<span class="hljs-keyword">add_custom_command</span>(
  OUTPUT <span class="hljs-variable">${CMAKE_BINARY_DIR}</span>/generated.cpp
  <span class="hljs-keyword">COMMAND</span> python3 <span class="hljs-variable">${CMAKE_SOURCE_DIR}</span>/gen.py &gt; generated.cpp
  DEPENDS gen.py
  COMMENT <span class="hljs-string">&quot;Generating code&quot;</span>
  VERBATIM)
<span class="hljs-keyword">add_custom_target</span>(docs ALL DEPENDS generated.cpp)
<span class="hljs-keyword">execute_process</span>(<span class="hljs-keyword">COMMAND</span> hg id OUTPUT_VARIABLE SHA OUTPUT_STRIP_TRAILING_WHITESPACE)
""");
    }

    [Fact]
    public void PackageConfigTemplate_CMakeInAlias()
    {
        AssertHighlighter("cmake.in",
"""
@PACKAGE_INIT@

include("${CMAKE_CURRENT_LIST_DIR}/FooTargets.cmake")
set_and_check(FOO_INCLUDE_DIR "@PACKAGE_INCLUDE_INSTALL_DIR@")
check_required_components(Foo)
""",
"""
@PACKAGE_INIT@

<span class="hljs-keyword">include</span>(<span class="hljs-string">&quot;<span class="hljs-variable">${CMAKE_CURRENT_LIST_DIR}</span>/FooTargets.cmake&quot;</span>)
set_and_check(FOO_INCLUDE_DIR <span class="hljs-string">&quot;@PACKAGE_INCLUDE_INSTALL_DIR@&quot;</span>)
check_required_components(Foo)
""");
    }

    [Fact]
    public void KeywordsInsideIdentifiers()
    {
        AssertHighlighter("cmake",
"""
set(target_name mytarget)
set(test_dir tests)
set_property(TARGET app PROPERTY CXX_STANDARD 17)
set_target_properties(app PROPERTIES OUTPUT_NAME "my-app")
string(TOUPPER "${name}" NAME_UPPER)
string(REGEX REPLACE "[.]" "_" out "${in}")
file(GLOB_RECURSE SRCS CONFIGURE_DEPENDS src/*.cpp)
""",
"""
<span class="hljs-keyword">set</span>(target_name mytarget)
<span class="hljs-keyword">set</span>(test_dir tests)
<span class="hljs-keyword">set_property</span>(<span class="hljs-keyword">TARGET</span> app PROPERTY CXX_STANDARD <span class="hljs-number">17</span>)
<span class="hljs-keyword">set_target_properties</span>(app PROPERTIES OUTPUT_NAME <span class="hljs-string">&quot;my-app&quot;</span>)
<span class="hljs-keyword">string</span>(TOUPPER <span class="hljs-string">&quot;<span class="hljs-variable">${name}</span>&quot;</span> NAME_UPPER)
<span class="hljs-keyword">string</span>(REGEX REPLACE <span class="hljs-string">&quot;[.]&quot;</span> <span class="hljs-string">&quot;_&quot;</span> out <span class="hljs-string">&quot;<span class="hljs-variable">${in}</span>&quot;</span>)
<span class="hljs-keyword">file</span>(GLOB_RECURSE SRCS CONFIGURE_DEPENDS src/*.cpp)
""");
    }

    [Fact]
    public void CTestCommands()
    {
        AssertHighlighter("cmake",
"""
enable_testing()
include(CTest)
include(FetchContent)
FetchContent_Declare(googletest URL https://github.com/google/googletest/archive/v1.14.0.zip)
FetchContent_MakeAvailable(googletest)
ctest_start(Experimental)
ctest_build()
ctest_test()
""",
"""
<span class="hljs-keyword">enable_testing</span>()
<span class="hljs-keyword">include</span>(CTest)
<span class="hljs-keyword">include</span>(FetchContent)
FetchContent_Declare(googletest URL https://github.com/google/googletest/archive/v1.<span class="hljs-number">14.0</span>.zip)
FetchContent_MakeAvailable(googletest)
<span class="hljs-keyword">ctest_start</span>(Experimental)
<span class="hljs-keyword">ctest_build</span>()
<span class="hljs-keyword">ctest_test</span>()
""");
    }

    [Fact]
    public void GeneratorExpressionsArePlain()
    {
        AssertHighlighter("cmake",
"""
target_compile_options(app PRIVATE $<$<CXX_COMPILER_ID:MSVC>:/W4> $<$<NOT:$<CXX_COMPILER_ID:MSVC>>:-Wall>)
""",
"""
<span class="hljs-keyword">target_compile_options</span>(app PRIVATE $&lt;$&lt;CXX_COMPILER_ID:MSVC&gt;:/W4&gt; $&lt;$&lt;<span class="hljs-keyword">NOT</span>:$&lt;CXX_COMPILER_ID:MSVC&gt;&gt;:-Wall&gt;)
""");
    }

    [Fact]
    public void UnterminatedString()
    {
        AssertHighlighter("cmake",
"""
set(A "unterminated
set(B 1)
""",
"""
<span class="hljs-keyword">set</span>(A <span class="hljs-string">&quot;unterminated
set(B 1)</span>
""");
    }

    [Fact]
    public void UnterminatedBracketComment()
    {
        AssertHighlighter("cmake",
"""
#[[ never closed
set(A 1)
""",
"""
<span class="hljs-comment">#[[ never closed
set(A 1)</span>
""");
    }

    [Fact]
    public void UnterminatedVariableReference()
    {
        AssertHighlighter("cmake",
"""
set(A ${oops
set(B 1)
""",
"""
<span class="hljs-keyword">set</span>(A <span class="hljs-variable">${oops
set(B 1)</span>
""");
    }

    [Fact]
    public void EscapedNewline()
    {
        AssertHighlighter("cmake",
"""
message("a \
b")
""",
"""
<span class="hljs-keyword">message</span>(<span class="hljs-string">&quot;a \
b&quot;</span>)
""");
    }

    [Fact]
    public void DeeplyNestedVariableReferences()
    {
        var code = string.Concat(Enumerable.Repeat("${", 20_000)) + "x" + new string('}', 20_000) + " set(a 1)";

        var result = HighlightWithFallbackDetection(code, "cmake", out var isFallback);

        Assert.False(isFallback);
        Assert.StartsWith("<span class=\"hljs-variable\">${<span class=\"hljs-variable\">${", result, StringComparison.Ordinal);
        Assert.EndsWith("}</span>}</span> <span class=\"hljs-keyword\">set</span>(a <span class=\"hljs-number\">1</span>)", result, StringComparison.Ordinal);
    }
}
