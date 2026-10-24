// cheerio (cheerio/slim, on htmlparser2): jQuery-like selection with css-select, traversal, manipulation and rendering
import { load } from "cheerio/slim";

const html = `<html><head><title>Shop</title></head><body>
<nav><a href="/" class="active">Home</a><a href="/products">Products</a></nav>
<ul id="products">
  <li class="product" data-id="1" data-price="12.5"><h2>Book</h2><span class="tag">paper</span></li>
  <li class="product sale" data-id="2" data-price="3"><h2>Pen</h2><span class="tag">ink</span><span class="tag">blue</span></li>
  <li class="product" data-id="3" data-price="30"><h2>Bag</h2></li>
</ul>
<form><input name="q" value="search"><input type="checkbox" name="c" checked><select name="s"><option value="1">1<option value="2" selected>2</select></form>
</body></html>`;

export default () => {
  const $ = load(html);
  const results = [];
  results.push($("li.product").map((index, element) => $(element).find("h2").text() + "=" + $(element).data("price")).get().join(", "));
  results.push($("li:has(.tag + .tag) h2").text(), $("li:not(.sale):last-child").attr("data-id"), $("a[href^='/p']").text(), $("li:nth-child(2n+1) h2").map((index, element) => $(element).text()).get().join());
  results.push($("ul > li").first().next().children("span").last().text(), $(".tag").parents("li").length, $("h2").filter((index, element) => $(element).text().length === 3).length);
  results.push(JSON.stringify($("form").serializeArray()), $("input[name=q]").val(), $("select").val());
  $("li.sale").addClass("highlight").removeClass("sale").attr("title", "On sale").css("color", "red");
  $("#products").append("<li class='product new'><h2>Cup</h2></li>").prepend("<!-- start -->");
  $("li[data-id=3]").remove();
  $("h2").each((index, element) => { $(element).text($(element).text().toUpperCase()); });
  $("nav a").wrap("<span class='link'></span>");
  results.push($("#products").html().trim(), $("nav").html(), $.text($("title")), $("body").children().length);
  results.push($.root().find("li").length, $.html($("li.new")), load("<p>fragment</p>", null, false).html());
  return results.join("\n----\n");
};
