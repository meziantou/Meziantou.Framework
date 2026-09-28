namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class GroovyHighlighterTests
{
    [Fact]
    public void AssertPower()
    {
        AssertHighlighter("groovy",
"""
assert 1 + 1 == 2
assert [1, 2, 3].size() == 3 : "size mismatch"
def x = 5
assert x instanceof Integer
""",
"""
<span class="hljs-keyword">assert</span> <span class="hljs-number">1</span> + <span class="hljs-number">1</span> == <span class="hljs-number">2</span>
<span class="hljs-keyword">assert</span> [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>].size() == <span class="hljs-number">3</span> : <span class="hljs-string">&quot;size mismatch&quot;</span>
<span class="hljs-keyword">def</span> x = <span class="hljs-number">5</span>
<span class="hljs-keyword">assert</span> x <span class="hljs-keyword">instanceof</span> Integer
""");
    }

    [Fact]
    public void BuilderDsl()
    {
        AssertHighlighter("groovy",
"""
def writer = new StringWriter()
def html = new groovy.xml.MarkupBuilder(writer)
html.html {
    head { title 'Page' }
    body(class: 'main') {
        h1 'Hello'
        p(id: 'intro', 'Welcome')
    }
}
""",
"""
<span class="hljs-keyword">def</span> writer = <span class="hljs-keyword">new</span> StringWriter()
<span class="hljs-keyword">def</span> html = <span class="hljs-keyword">new</span> groovy.xml.MarkupBuilder(writer)
html.html {
    head { title <span class="hljs-string">&#x27;Page&#x27;</span> }
    body(<span class="hljs-attr">class:</span> <span class="hljs-string">&#x27;main&#x27;</span>) {
        h1 <span class="hljs-string">&#x27;Hello&#x27;</span>
        p(<span class="hljs-attr">id:</span> <span class="hljs-string">&#x27;intro&#x27;</span>, <span class="hljs-string">&#x27;Welcome&#x27;</span>)
    }
}
""");
    }

    [Fact]
    public void Classes()
    {
        AssertHighlighter("groovy",
"""
package com.example

import groovy.transform.CompileStatic
import java.util.concurrent.*

@CompileStatic
abstract class Shape implements Comparable<Shape>, Serializable {
    abstract double area()

    int compareTo(Shape other) {
        return area() <=> other.area()
    }
}

class Circle extends Shape {
    final double radius

    Circle(double radius) {
        this.radius = radius
    }

    @Override
    double area() { Math.PI * radius ** 2 }
}

interface Drawable {
    void draw()
}

trait Flying {
    String fly() { "I'm flying!" }
}

enum Color { RED, GREEN, BLUE }

record Point(int x, int y) {}
""",
"""
<span class="hljs-keyword">package</span> com.example

<span class="hljs-keyword">import</span> groovy.transform.CompileStatic
<span class="hljs-keyword">import</span> java.util.concurrent.*

<span class="hljs-meta">@CompileStatic</span>
<span class="hljs-keyword">abstract</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">Shape</span> <span class="hljs-keyword">implements</span> <span class="hljs-title class_">Comparable</span>&lt;Shape&gt;, Serializable {
    <span class="hljs-keyword">abstract</span> <span class="hljs-type">double</span> area()

    <span class="hljs-type">int</span> compareTo(Shape other) {
        <span class="hljs-keyword">return</span> area() &lt;=&gt; other.area()
    }
}

<span class="hljs-keyword">class</span> <span class="hljs-title class_">Circle</span> <span class="hljs-keyword">extends</span> <span class="hljs-title class_">Shape</span> {
    <span class="hljs-keyword">final</span> <span class="hljs-type">double</span> radius

    Circle(<span class="hljs-type">double</span> radius) {
        <span class="hljs-variable language_">this</span>.radius = radius
    }

    <span class="hljs-meta">@Override</span>
    <span class="hljs-type">double</span> area() { Math.PI * radius ** <span class="hljs-number">2</span> }
}

<span class="hljs-keyword">interface</span> <span class="hljs-title class_">Drawable</span> {
    <span class="hljs-type">void</span> draw()
}

<span class="hljs-keyword">trait</span> <span class="hljs-title class_">Flying</span> {
    String fly() { <span class="hljs-string">&quot;I&#x27;m flying!&quot;</span> }
}

<span class="hljs-keyword">enum</span> <span class="hljs-title class_">Color</span> { RED, GREEN, BLUE }

<span class="hljs-keyword">record</span> <span class="hljs-title class_">Point</span>(<span class="hljs-type">int</span> x, <span class="hljs-type">int</span> y) {}
""");
    }

    [Fact]
    public void Closures()
    {
        AssertHighlighter("groovy",
"""
def square = { it * it }
def add = { a, b -> a + b }
def list = [1, 2, 3, 4]
list.each { println it }
list.collect { it * 2 }.findAll { it > 4 }
def result = list.inject(0) { acc, val -> acc + val }
[1, 2, 3].eachWithIndex { item, idx -> println "$idx: $item" }
""",
"""
<span class="hljs-keyword">def</span> square = { it * it }
<span class="hljs-keyword">def</span> add = { a, b -&gt; a + b }
<span class="hljs-keyword">def</span> list = [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>, <span class="hljs-number">4</span>]
list.each { println it }
list.collect { it * <span class="hljs-number">2</span> }.findAll { it &gt; <span class="hljs-number">4</span> }
<span class="hljs-keyword">def</span> result = list.inject(<span class="hljs-number">0</span>) { acc, val -&gt; acc + val }
[<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>].eachWithIndex { item, idx -&gt; println <span class="hljs-string">&quot;$idx: $item&quot;</span> }
""");
    }

    [Fact]
    public void Collections()
    {
        AssertHighlighter("groovy",
"""
def list = [1, 2, 3]
def map = [name: 'Groovy', version: 4, 'quoted key': true]
def empty = [:]
def range = 1..10
def exclusive = 1..<10
map.each { key, value -> println "$key = $value" }
assert map.name == 'Groovy'
""",
"""
<span class="hljs-keyword">def</span> list = [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>]
<span class="hljs-keyword">def</span> map = [<span class="hljs-attr">name:</span> <span class="hljs-string">&#x27;Groovy&#x27;</span>, <span class="hljs-attr">version:</span> <span class="hljs-number">4</span>, <span class="hljs-string">&#x27;quoted key&#x27;</span>: <span class="hljs-literal">true</span>]
<span class="hljs-keyword">def</span> empty = [:]
<span class="hljs-keyword">def</span> range = <span class="hljs-number">1</span>..<span class="hljs-number">10</span>
<span class="hljs-keyword">def</span> exclusive = <span class="hljs-number">1</span>..&lt;<span class="hljs-number">10</span>
map.each { key, value -&gt; println <span class="hljs-string">&quot;$key = $value&quot;</span> }
<span class="hljs-keyword">assert</span> map.name == <span class="hljs-string">&#x27;Groovy&#x27;</span>
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("groovy",
"""
// line comment TODO: fix
/* block comment */
/**
 * Groovydoc for the class.
 * Contact jane@example.com
 * @param name the name
 * @return the greeting
 */
def x = 1 // trailing
""",
"""
<span class="hljs-comment">// line comment <span class="hljs-doctag">TODO:</span> fix</span>
<span class="hljs-comment">/* block comment */</span>
<span class="hljs-comment">/**
 * Groovydoc for the class.
 * Contact jane@example.com
 * <span class="hljs-doctag">@param</span> name the name
 * <span class="hljs-doctag">@return</span> the greeting
 */</span>
<span class="hljs-keyword">def</span> x = <span class="hljs-number">1</span> <span class="hljs-comment">// trailing</span>
""");
    }

    [Fact]
    public void ControlFlow()
    {
        AssertHighlighter("groovy",
"""
def check(x) {
    if (x instanceof String) {
        return "string"
    } else if (x in [1, 2, 3]) {
        return "small"
    }
    switch (x) {
        case Integer:
            println 'int'
            break
        case ~/\d+/:
            println 'digits'
            break
        default:
            println 'other'
    }
    for (i in 0..<3) { println i }
    while (x > 0) { x-- }
    try {
        risky()
    } catch (IOException | IllegalStateException e) {
        throw new RuntimeException(e)
    } finally {
        cleanup()
    }
}
""",
"""
<span class="hljs-keyword">def</span> check(x) {
    <span class="hljs-keyword">if</span> (x <span class="hljs-keyword">instanceof</span> String) {
        <span class="hljs-keyword">return</span> <span class="hljs-string">&quot;string&quot;</span>
    } <span class="hljs-keyword">else</span> <span class="hljs-keyword">if</span> (x <span class="hljs-keyword">in</span> [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>]) {
        <span class="hljs-keyword">return</span> <span class="hljs-string">&quot;small&quot;</span>
    }
    <span class="hljs-keyword">switch</span> (x) {
        <span class="hljs-keyword">case</span> <span class="hljs-attr">Integer:</span>
            println <span class="hljs-string">&#x27;int&#x27;</span>
            <span class="hljs-keyword">break</span>
        <span class="hljs-keyword">case</span> <span class="hljs-regexp">~/\d+/</span>:
            println <span class="hljs-string">&#x27;digits&#x27;</span>
            <span class="hljs-keyword">break</span>
        <span class="hljs-symbol">default:</span>
            println <span class="hljs-string">&#x27;other&#x27;</span>
    }
    <span class="hljs-keyword">for</span> (i <span class="hljs-keyword">in</span> <span class="hljs-number">0</span>..&lt;<span class="hljs-number">3</span>) { println i }
    <span class="hljs-keyword">while</span> (x &gt; <span class="hljs-number">0</span>) { x-- }
    <span class="hljs-keyword">try</span> {
        risky()
    } <span class="hljs-keyword">catch</span> (IOException | IllegalStateException e) {
        <span class="hljs-keyword">throw</span> <span class="hljs-keyword">new</span> RuntimeException(e)
    } <span class="hljs-keyword">finally</span> {
        cleanup()
    }
}
""");
    }

    [Fact]
    public void Division()
    {
        AssertHighlighter("groovy",
"""
def avg = total / count
def ratio = a / b / c
def half = x/2
""",
"""
<span class="hljs-keyword">def</span> avg = total / count
<span class="hljs-keyword">def</span> ratio = a <span class="hljs-regexp">/ b /</span> c
<span class="hljs-keyword">def</span> half = x/<span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void GradleAndroid()
    {
        AssertHighlighter("groovy",
"""
apply plugin: 'com.android.application'

android {
    compileSdkVersion 34
    defaultConfig {
        applicationId "com.example.app"
        minSdkVersion 24
        targetSdkVersion 34
        versionCode 1
        versionName "1.0"
        testInstrumentationRunner "androidx.test.runner.AndroidJUnitRunner"
    }
    buildTypes {
        release {
            minifyEnabled true
            proguardFiles getDefaultProguardFile('proguard-android-optimize.txt'), 'proguard-rules.pro'
        }
    }
    compileOptions {
        sourceCompatibility JavaVersion.VERSION_1_8
        targetCompatibility JavaVersion.VERSION_1_8
    }
}

dependencies {
    implementation fileTree(dir: 'libs', include: ['*.jar'])
    implementation "androidx.appcompat:appcompat:$appcompat_version"
}
""",
"""
apply <span class="hljs-attr">plugin:</span> <span class="hljs-string">&#x27;com.android.application&#x27;</span>

android {
    compileSdkVersion <span class="hljs-number">34</span>
    defaultConfig {
        applicationId <span class="hljs-string">&quot;com.example.app&quot;</span>
        minSdkVersion <span class="hljs-number">24</span>
        targetSdkVersion <span class="hljs-number">34</span>
        versionCode <span class="hljs-number">1</span>
        versionName <span class="hljs-string">&quot;1.0&quot;</span>
        testInstrumentationRunner <span class="hljs-string">&quot;androidx.test.runner.AndroidJUnitRunner&quot;</span>
    }
    buildTypes {
        release {
            minifyEnabled <span class="hljs-literal">true</span>
            proguardFiles getDefaultProguardFile(<span class="hljs-string">&#x27;proguard-android-optimize.txt&#x27;</span>), <span class="hljs-string">&#x27;proguard-rules.pro&#x27;</span>
        }
    }
    compileOptions {
        sourceCompatibility JavaVersion.VERSION_1_8
        targetCompatibility JavaVersion.VERSION_1_8
    }
}

dependencies {
    implementation fileTree(<span class="hljs-attr">dir:</span> <span class="hljs-string">&#x27;libs&#x27;</span>, <span class="hljs-attr">include:</span> [<span class="hljs-string">&#x27;*.jar&#x27;</span>])
    implementation <span class="hljs-string">&quot;androidx.appcompat:appcompat:$appcompat_version&quot;</span>
}
""");
    }

    [Fact]
    public void GradleBuild()
    {
        AssertHighlighter("groovy",
"""
plugins {
    id 'java'
    id 'application'
    id 'org.springframework.boot' version '3.2.0'
    id 'io.spring.dependency-management' version '1.1.4'
}

group = 'com.example'
version = '0.0.1-SNAPSHOT'

java {
    sourceCompatibility = JavaVersion.VERSION_17
}

repositories {
    mavenCentral()
    maven { url 'https://repo.spring.io/milestone' }
}

dependencies {
    implementation 'org.springframework.boot:spring-boot-starter-web'
    implementation("com.google.guava:guava:32.1.3-jre")
    compileOnly 'org.projectlombok:lombok'
    annotationProcessor 'org.projectlombok:lombok'
    testImplementation 'org.springframework.boot:spring-boot-starter-test'
    testImplementation platform('org.junit:junit-bom:5.10.0')
}

application {
    mainClass = 'com.example.Application'
}

tasks.named('test') {
    useJUnitPlatform()
}
""",
"""
plugins {
    id <span class="hljs-string">&#x27;java&#x27;</span>
    id <span class="hljs-string">&#x27;application&#x27;</span>
    id <span class="hljs-string">&#x27;org.springframework.boot&#x27;</span> version <span class="hljs-string">&#x27;3.2.0&#x27;</span>
    id <span class="hljs-string">&#x27;io.spring.dependency-management&#x27;</span> version <span class="hljs-string">&#x27;1.1.4&#x27;</span>
}

group = <span class="hljs-string">&#x27;com.example&#x27;</span>
version = <span class="hljs-string">&#x27;0.0.1-SNAPSHOT&#x27;</span>

java {
    sourceCompatibility = JavaVersion.VERSION_17
}

repositories {
    mavenCentral()
    maven { url <span class="hljs-string">&#x27;https://repo.spring.io/milestone&#x27;</span> }
}

dependencies {
    implementation <span class="hljs-string">&#x27;org.springframework.boot:spring-boot-starter-web&#x27;</span>
    implementation(<span class="hljs-string">&quot;com.google.guava:guava:32.1.3-jre&quot;</span>)
    compileOnly <span class="hljs-string">&#x27;org.projectlombok:lombok&#x27;</span>
    annotationProcessor <span class="hljs-string">&#x27;org.projectlombok:lombok&#x27;</span>
    testImplementation <span class="hljs-string">&#x27;org.springframework.boot:spring-boot-starter-test&#x27;</span>
    testImplementation platform(<span class="hljs-string">&#x27;org.junit:junit-bom:5.10.0&#x27;</span>)
}

application {
    mainClass = <span class="hljs-string">&#x27;com.example.Application&#x27;</span>
}

tasks.named(<span class="hljs-string">&#x27;test&#x27;</span>) {
    useJUnitPlatform()
}
""");
    }

    [Fact]
    public void GradleMultiproject()
    {
        AssertHighlighter("groovy",
"""
subprojects {
    apply plugin: 'java-library'

    ext {
        junitVersion = '5.10.0'
    }

    dependencies {
        testImplementation "org.junit.jupiter:junit-jupiter:${junitVersion}"
    }
}

project(':web') {
    dependencies {
        implementation project(':core')
    }
}

configurations.all {
    resolutionStrategy.cacheChangingModulesFor 0, 'seconds'
}
""",
"""
subprojects {
    apply <span class="hljs-attr">plugin:</span> <span class="hljs-string">&#x27;java-library&#x27;</span>

    ext {
        junitVersion = <span class="hljs-string">&#x27;5.10.0&#x27;</span>
    }

    dependencies {
        testImplementation <span class="hljs-string">&quot;org.junit.jupiter:junit-jupiter:${junitVersion}&quot;</span>
    }
}

project(<span class="hljs-string">&#x27;:web&#x27;</span>) {
    dependencies {
        implementation project(<span class="hljs-string">&#x27;:core&#x27;</span>)
    }
}

configurations.all {
    resolutionStrategy.cacheChangingModulesFor <span class="hljs-number">0</span>, <span class="hljs-string">&#x27;seconds&#x27;</span>
}
""");
    }

    [Fact]
    public void GradleSettings()
    {
        AssertHighlighter("groovy",
"""
rootProject.name = 'my-app'
include 'core', 'web'
include(':app')

pluginManagement {
    repositories {
        gradlePluginPortal()
        google()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
}
""",
"""
rootProject.name = <span class="hljs-string">&#x27;my-app&#x27;</span>
include <span class="hljs-string">&#x27;core&#x27;</span>, <span class="hljs-string">&#x27;web&#x27;</span>
include(<span class="hljs-string">&#x27;:app&#x27;</span>)

pluginManagement {
    repositories {
        gradlePluginPortal()
        google()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
}
""");
    }

    [Fact]
    public void GradleTasks()
    {
        AssertHighlighter("groovy",
"""
def buildTime = new Date().format('yyyy-MM-dd')

task hello {
    group = 'custom'
    description = 'Prints hello'
    doLast {
        println "Hello from ${project.name} at $buildTime"
    }
}

tasks.register('copyDocs', Copy) {
    from file("$buildDir/docs")
    into layout.buildDirectory.dir('out')
    include '**/*.html'
    exclude { details -> details.file.name.endsWith('.tmp') }
}

jar {
    manifest {
        attributes(
            'Implementation-Title': project.name,
            'Implementation-Version': project.version
        )
    }
}

test {
    maxParallelForks = Runtime.runtime.availableProcessors().intdiv(2) ?: 1
    testLogging {
        events 'passed', 'skipped', 'failed'
    }
}

if (project.hasProperty('release')) {
    version = version.replace('-SNAPSHOT', '')
}
""",
"""
<span class="hljs-keyword">def</span> buildTime = <span class="hljs-keyword">new</span> Date().format(<span class="hljs-string">&#x27;yyyy-MM-dd&#x27;</span>)

task hello {
    group = <span class="hljs-string">&#x27;custom&#x27;</span>
    description = <span class="hljs-string">&#x27;Prints hello&#x27;</span>
    doLast {
        println <span class="hljs-string">&quot;Hello from ${project.name} at $buildTime&quot;</span>
    }
}

tasks.register(<span class="hljs-string">&#x27;copyDocs&#x27;</span>, Copy) {
    from file(<span class="hljs-string">&quot;$buildDir/docs&quot;</span>)
    into layout.buildDirectory.dir(<span class="hljs-string">&#x27;out&#x27;</span>)
    include <span class="hljs-string">&#x27;**/*.html&#x27;</span>
    exclude { details -&gt; details.file.name.endsWith(<span class="hljs-string">&#x27;.tmp&#x27;</span>) }
}

jar {
    manifest {
        attributes(
            <span class="hljs-string">&#x27;Implementation-Title&#x27;</span>: project.name,
            <span class="hljs-string">&#x27;Implementation-Version&#x27;</span>: project.version
        )
    }
}

test {
    maxParallelForks = Runtime.runtime.availableProcessors().intdiv(<span class="hljs-number">2</span>) ?: <span class="hljs-number">1</span>
    testLogging {
        events <span class="hljs-string">&#x27;passed&#x27;</span>, <span class="hljs-string">&#x27;skipped&#x27;</span>, <span class="hljs-string">&#x27;failed&#x27;</span>
    }
}

<span class="hljs-keyword">if</span> (project.hasProperty(<span class="hljs-string">&#x27;release&#x27;</span>)) {
    version = version.replace(<span class="hljs-string">&#x27;-SNAPSHOT&#x27;</span>, <span class="hljs-string">&#x27;&#x27;</span>)
}
""");
    }

    [Fact]
    public void GstringEdge()
    {
        AssertHighlighter("groovy",
""""
def s1 = "Total: ${items.sum { it.price }}"
def s2 = "Path: $dir/$file"
def s3 = "Escaped \${notInterpolated}"
def s4 = "a" + 'b' + """c"""
"""",
"""
<span class="hljs-keyword">def</span> s1 = <span class="hljs-string">&quot;Total: ${items.sum { it.price }}&quot;</span>
<span class="hljs-keyword">def</span> s2 = <span class="hljs-string">&quot;Path: $dir/$file&quot;</span>
<span class="hljs-keyword">def</span> s3 = <span class="hljs-string">&quot;Escaped \${notInterpolated}&quot;</span>
<span class="hljs-keyword">def</span> s4 = <span class="hljs-string">&quot;a&quot;</span> + <span class="hljs-string">&#x27;b&#x27;</span> + <span class="hljs-string">&quot;&quot;&quot;c&quot;&quot;&quot;</span>
""");
    }

    [Fact]
    public void Hello()
    {
        AssertHighlighter("groovy",
"""
class Greeter {
    String name

    def greet() {
        println "Hello, ${name}!"
    }
}

new Greeter(name: 'World').greet()
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Greeter</span> {
    String name

    <span class="hljs-keyword">def</span> greet() {
        println <span class="hljs-string">&quot;Hello, ${name}!&quot;</span>
    }
}

<span class="hljs-keyword">new</span> Greeter(<span class="hljs-attr">name:</span> <span class="hljs-string">&#x27;World&#x27;</span>).greet()
""");
    }

    [Fact]
    public void IllegalHash()
    {
        AssertHighlighter("groovy",
"""
def a = 1 # comment?
def b = 2
""",
"""
<span class="hljs-keyword">def</span> a = <span class="hljs-number">1</span> # comment?
def b = <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void Jenkinsfile()
    {
        AssertHighlighter("groovy",
"""
pipeline {
    agent any
    environment {
        APP_NAME = 'demo'
    }
    stages {
        stage('Build') {
            steps {
                sh './gradlew build'
            }
        }
        stage('Test') {
            when {
                branch 'main'
            }
            steps {
                sh "./gradlew test -Pversion=${env.BUILD_NUMBER}"
                junit '**/build/test-results/**/*.xml'
            }
        }
    }
    post {
        always {
            cleanWs()
        }
    }
}
""",
"""
pipeline {
    agent any
    environment {
        APP_NAME = <span class="hljs-string">&#x27;demo&#x27;</span>
    }
    stages {
        stage(<span class="hljs-string">&#x27;Build&#x27;</span>) {
            steps {
                sh <span class="hljs-string">&#x27;./gradlew build&#x27;</span>
            }
        }
        stage(<span class="hljs-string">&#x27;Test&#x27;</span>) {
            when {
                branch <span class="hljs-string">&#x27;main&#x27;</span>
            }
            steps {
                sh <span class="hljs-string">&quot;./gradlew test -Pversion=${env.BUILD_NUMBER}&quot;</span>
                junit <span class="hljs-string">&#x27;**/build/test-results/**/*.xml&#x27;</span>
            }
        }
    }
    post {
        always {
            cleanWs()
        }
    }
}
""");
    }

    [Fact]
    public void Labels()
    {
        AssertHighlighter("groovy",
"""
outer:
for (i in 0..10) {
    inner:
    for (j in 0..10) {
        if (j == 5) continue outer
        if (i == 5) break outer
    }
}
""",
"""
<span class="hljs-attr">outer:</span>
<span class="hljs-keyword">for</span> (i <span class="hljs-keyword">in</span> <span class="hljs-number">0</span>..<span class="hljs-number">10</span>) {
    <span class="hljs-symbol">inner:</span>
    <span class="hljs-keyword">for</span> (j <span class="hljs-keyword">in</span> <span class="hljs-number">0</span>..<span class="hljs-number">10</span>) {
        <span class="hljs-keyword">if</span> (j == <span class="hljs-number">5</span>) <span class="hljs-keyword">continue</span> outer
        <span class="hljs-keyword">if</span> (i == <span class="hljs-number">5</span>) <span class="hljs-keyword">break</span> outer
    }
}
""");
    }

    [Fact]
    public void MethodsTypes()
    {
        AssertHighlighter("groovy",
"""
static void main(String[] args) {
    int count = 0
    long total = 0L
    boolean flag = false
    def dynamic = null
    var inferred = "x"
    char c = 'a'
    byte b = 1
    short s = 2
    float f = 1.0f
    double dd = 2.0d
}

private String format(Map<String, Object> params, int indent = 2) {
    return params.collect { k, v -> "$k=$v" }.join(', ')
}

public synchronized List<Integer> numbers() { [] }
""",
"""
<span class="hljs-keyword">static</span> <span class="hljs-type">void</span> main(String[] args) {
    <span class="hljs-type">int</span> count = <span class="hljs-number">0</span>
    <span class="hljs-type">long</span> total = <span class="hljs-number">0L</span>
    <span class="hljs-type">boolean</span> flag = <span class="hljs-literal">false</span>
    <span class="hljs-keyword">def</span> dynamic = <span class="hljs-literal">null</span>
    <span class="hljs-keyword">var</span> inferred = <span class="hljs-string">&quot;x&quot;</span>
    <span class="hljs-type">char</span> c = <span class="hljs-string">&#x27;a&#x27;</span>
    <span class="hljs-type">byte</span> b = <span class="hljs-number">1</span>
    <span class="hljs-type">short</span> s = <span class="hljs-number">2</span>
    <span class="hljs-type">float</span> f = <span class="hljs-number">1.0f</span>
    <span class="hljs-type">double</span> dd = <span class="hljs-number">2.0d</span>
}

<span class="hljs-keyword">private</span> String format(Map&lt;String, Object&gt; params, <span class="hljs-type">int</span> indent = <span class="hljs-number">2</span>) {
    <span class="hljs-keyword">return</span> params.collect { k, v -&gt; <span class="hljs-string">&quot;$k=$v&quot;</span> }.join(<span class="hljs-string">&#x27;, &#x27;</span>)
}

<span class="hljs-keyword">public</span> <span class="hljs-keyword">synchronized</span> List&lt;Integer&gt; numbers() { [] }
""");
    }

    [Fact]
    public void NamedArgs()
    {
        AssertHighlighter("groovy",
"""
def person = new Person(name: 'Alice', age: 30)
task copy(type: Copy) {
    from 'src'
    into 'dest'
}
method(key: value, other : 2)
""",
"""
<span class="hljs-keyword">def</span> person = <span class="hljs-keyword">new</span> Person(<span class="hljs-attr">name:</span> <span class="hljs-string">&#x27;Alice&#x27;</span>, <span class="hljs-attr">age:</span> <span class="hljs-number">30</span>)
task copy(<span class="hljs-attr">type:</span> Copy) {
    from <span class="hljs-string">&#x27;src&#x27;</span>
    into <span class="hljs-string">&#x27;dest&#x27;</span>
}
method(<span class="hljs-attr">key:</span> value, <span class="hljs-attr">other :</span> <span class="hljs-number">2</span>)
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("groovy",
"""
def a = 42
def b = 3.14
def c = 1e10
def d = 0xFF
def e = 0b1010
def f = 100L
def g = 1.5G
def h = 2.5f
def i = -7
def j = 1_000_000
""",
"""
<span class="hljs-keyword">def</span> a = <span class="hljs-number">42</span>
<span class="hljs-keyword">def</span> b = <span class="hljs-number">3.14</span>
<span class="hljs-keyword">def</span> c = <span class="hljs-number">1e10</span>
<span class="hljs-keyword">def</span> d = <span class="hljs-number">0xFF</span>
<span class="hljs-keyword">def</span> e = <span class="hljs-number">0b1010</span>
<span class="hljs-keyword">def</span> f = <span class="hljs-number">100L</span>
<span class="hljs-keyword">def</span> g = <span class="hljs-number">1.5G</span>
<span class="hljs-keyword">def</span> h = <span class="hljs-number">2.5f</span>
<span class="hljs-keyword">def</span> i = <span class="hljs-number">-7</span>
<span class="hljs-keyword">def</span> j = <span class="hljs-number">1_000_000</span>
""");
    }

    [Fact]
    public void NumericLiterals()
    {
        AssertHighlighter("groovy",
"""
def a = 1_000_000
def b = 100L
def c = 1.5G
def d = 42G
def e = 2.5f
def f = 3.0d
def g = 0xFF_FFL
def h = 0b1010_1010
def i = 10i
def r1 = 1..10
def r2 = 0..<size
def r3 = 1.5..2.5
def r4 = 'a'..'z'
def m = 5.plus(3)
""",
"""
<span class="hljs-keyword">def</span> a = <span class="hljs-number">1_000_000</span>
<span class="hljs-keyword">def</span> b = <span class="hljs-number">100L</span>
<span class="hljs-keyword">def</span> c = <span class="hljs-number">1.5G</span>
<span class="hljs-keyword">def</span> d = <span class="hljs-number">42G</span>
<span class="hljs-keyword">def</span> e = <span class="hljs-number">2.5f</span>
<span class="hljs-keyword">def</span> f = <span class="hljs-number">3.0d</span>
<span class="hljs-keyword">def</span> g = <span class="hljs-number">0xFF_FFL</span>
<span class="hljs-keyword">def</span> h = <span class="hljs-number">0b1010_1010</span>
<span class="hljs-keyword">def</span> i = <span class="hljs-number">10i</span>
<span class="hljs-keyword">def</span> r1 = <span class="hljs-number">1</span>..<span class="hljs-number">10</span>
<span class="hljs-keyword">def</span> r2 = <span class="hljs-number">0</span>..&lt;size
<span class="hljs-keyword">def</span> r3 = <span class="hljs-number">1.5</span>..<span class="hljs-number">2.5</span>
<span class="hljs-keyword">def</span> r4 = <span class="hljs-string">&#x27;a&#x27;</span>..<span class="hljs-string">&#x27;z&#x27;</span>
<span class="hljs-keyword">def</span> m = <span class="hljs-number">5</span>.plus(<span class="hljs-number">3</span>)
""");
    }

    [Fact]
    public void Operators()
    {
        AssertHighlighter("groovy",
"""
def a = b + c - d * e / f % g
def power = 2 ** 10
def spaceship = a <=> b
def spread = list*.name
def safe = obj?.prop
def elvis = x ?: y
def isSame = a.is(b)
def membership = x in list
def range = 1..5
def method = obj.&method
a += 1; a -= 1; a *= 2
""",
"""
<span class="hljs-keyword">def</span> a = b + c - d * e / f % g
<span class="hljs-keyword">def</span> power = <span class="hljs-number">2</span> ** <span class="hljs-number">10</span>
<span class="hljs-keyword">def</span> spaceship = a &lt;=&gt; b
<span class="hljs-keyword">def</span> spread = list*.name
<span class="hljs-keyword">def</span> safe = obj?.prop
<span class="hljs-keyword">def</span> elvis = x ?: y
<span class="hljs-keyword">def</span> isSame = a.is(b)
<span class="hljs-keyword">def</span> membership = x <span class="hljs-keyword">in</span> list
<span class="hljs-keyword">def</span> range = <span class="hljs-number">1</span>..<span class="hljs-number">5</span>
<span class="hljs-keyword">def</span> method = obj.&amp;method
a += <span class="hljs-number">1</span>; a -= <span class="hljs-number">1</span>; a *= <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void Regex()
    {
        AssertHighlighter("groovy",
"""
def pattern = ~/[a-z]+\d*/
def matcher = text =~ /(\w+)@(\w+)\.com/
def matches = text ==~ /\d{3}-\d{4}/
def replaced = s.replaceAll(/\s+/, ' ')
""",
"""
<span class="hljs-keyword">def</span> pattern = <span class="hljs-regexp">~/[a-z]+\d*/</span>
<span class="hljs-keyword">def</span> matcher = text =~ <span class="hljs-regexp">/(\w+)@(\w+)\.com/</span>
<span class="hljs-keyword">def</span> matches = text ==~ <span class="hljs-regexp">/\d{3}-\d{4}/</span>
<span class="hljs-keyword">def</span> replaced = s.replaceAll(<span class="hljs-regexp">/\s+/</span>, <span class="hljs-string">&#x27; &#x27;</span>)
""");
    }

    [Fact]
    public void SafeNavigationElvis()
    {
        AssertHighlighter("groovy",
"""
def name = person?.name
def city = person?.address?.city ?: 'unknown'
def value = input ?: 'default'
def len = text?.length() ?: 0
if (user?.active) {
    return true
}
""",
"""
<span class="hljs-keyword">def</span> name = person?.name
<span class="hljs-keyword">def</span> city = person?.address?.city ?: <span class="hljs-string">&#x27;unknown&#x27;</span>
<span class="hljs-keyword">def</span> value = input ?: <span class="hljs-string">&#x27;default&#x27;</span>
<span class="hljs-keyword">def</span> len = text?.length() ?: <span class="hljs-number">0</span>
<span class="hljs-keyword">if</span> (user?.active) {
    <span class="hljs-keyword">return</span> <span class="hljs-literal">true</span>
}
""");
    }

    [Fact]
    public void Shebang()
    {
        AssertHighlighter("groovy",
"""
#!/usr/bin/env groovy
println 'script'
""",
"""
<span class="hljs-meta">#!/usr/bin/env groovy</span>
println <span class="hljs-string">&#x27;script&#x27;</span>
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("groovy",
""""
def single = 'single $notInterpolated'
def double = "double ${interpolated} and $simple"
def escaped = "tab\t \"quote\" \\"
def triple = """multi
line ${value}
"""
def tripleSingle = '''raw
multi'''
def slashy = /regex\d+/
def dollarSlashy = $/dollar
slashy/$
def ch = 'c' as char
"""",
"""
<span class="hljs-keyword">def</span> single = <span class="hljs-string">&#x27;single $notInterpolated&#x27;</span>
<span class="hljs-keyword">def</span> <span class="hljs-type">double</span> = <span class="hljs-string">&quot;double ${interpolated} and $simple&quot;</span>
<span class="hljs-keyword">def</span> escaped = <span class="hljs-string">&quot;tab\t \&quot;quote\&quot; \\&quot;</span>
<span class="hljs-keyword">def</span> triple = <span class="hljs-string">&quot;&quot;&quot;multi
line ${value}
&quot;&quot;&quot;</span>
<span class="hljs-keyword">def</span> tripleSingle = <span class="hljs-string">&#x27;&#x27;&#x27;raw
multi&#x27;&#x27;&#x27;</span>
<span class="hljs-keyword">def</span> slashy = <span class="hljs-regexp">/regex\d+/</span>
<span class="hljs-keyword">def</span> dollarSlashy = <span class="hljs-string">$/dollar
slashy/$</span>
<span class="hljs-keyword">def</span> ch = <span class="hljs-string">&#x27;c&#x27;</span> <span class="hljs-keyword">as</span> <span class="hljs-type">char</span>
""");
    }

    [Fact]
    public void Ternary()
    {
        AssertHighlighter("groovy",
"""
def type = x > 0 ? 'positive' : 'negative'
def max = a > b ? a : b
def nested = a ? (b ? 1 : 2) : 3
def withComment = flag ? /* yes */ 1 : 0
""",
"""
<span class="hljs-keyword">def</span> type = x &gt; <span class="hljs-number">0</span> ? <span class="hljs-string">&#x27;positive&#x27;</span> : <span class="hljs-string">&#x27;negative&#x27;</span>
<span class="hljs-keyword">def</span> max = a &gt; b ? a : b
<span class="hljs-keyword">def</span> nested = a ? (b ? <span class="hljs-number">1</span> : <span class="hljs-number">2</span>) : <span class="hljs-number">3</span>
<span class="hljs-keyword">def</span> withComment = flag ? <span class="hljs-comment">/* yes */</span> <span class="hljs-number">1</span> : <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void ThisSuper()
    {
        AssertHighlighter("groovy",
"""
class Child extends Parent {
    Child() {
        super()
        this.value = 1
    }
    def call() { super.call() + this.helper() }
}
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Child</span> <span class="hljs-keyword">extends</span> <span class="hljs-title class_">Parent</span> {
    Child() {
        <span class="hljs-variable language_">super</span>()
        <span class="hljs-variable language_">this</span>.value = <span class="hljs-number">1</span>
    }
    <span class="hljs-keyword">def</span> call() { <span class="hljs-variable language_">super</span>.call() + <span class="hljs-variable language_">this</span>.helper() }
}
""");
    }
}
